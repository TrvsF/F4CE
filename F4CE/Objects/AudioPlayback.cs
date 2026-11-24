using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace F4CE.Objects;

internal partial class OAudioPlayback
{
	public MemoryStream MemoryStream { get; } = new();

	public FPlaybackSettings PlaybackSettings { get; set; } = new();
	public bool IsRecording { get; private set; } = false;
	public bool IsPlaying { get; private set; } = false;

	public bool HasRecording { get => MemoryStream.Length > 0 && !IsRecording; }
	public long BaseLength { get => MemoryStream.Length; }

	public float PlaybackProgress
	{
		get
		{
			if (!IsPlaying || CachedTotalDuration == TimeSpan.Zero)
			{
				return 0f;
			}

			return (float)Math.Clamp(PlaybackStopwatch.Elapsed.TotalSeconds / CachedTotalDuration.TotalSeconds, 0f, 1f);
		}
	}

	private readonly Stopwatch PlaybackStopwatch = new();
	private TimeSpan CachedTotalDuration = TimeSpan.Zero;

	private WaveInEvent WaveIn;
	private WaveFileWriter Writer;

	public void SetSilence(TimeSpan Duration)
	{
		StopRecording();
		StopPlayback();

		WaveFormat Format = new(PlaybackManager.SampleRate, PlaybackManager.Channels);

		using MemoryStream TempStream = new();

		using (WaveFileWriter Writer = new(new IgnoreDisposeStream(TempStream), Format))
		{
			int BytesPerSecond = Format.AverageBytesPerSecond;
			int SilentBytes = (int)(BytesPerSecond * Duration.TotalSeconds);

			byte[] Buffer = new byte[SilentBytes];

			Writer.Write(Buffer, 0, Buffer.Length);
			Writer.Flush();
		}

		MemoryStream.Position = 0;
		MemoryStream.SetLength(0);

		TempStream.Position = 0;
		TempStream.CopyTo(MemoryStream);

		MemoryStream.Position = 0;
	}

	public void StartRecording()
	{
		StopPlayback();

		MemoryStream.Position = 0;
		MemoryStream.SetLength(0);

		WaveIn = new WaveInEvent
		{
			DeviceNumber = 0,
			WaveFormat = new WaveFormat(44100, 24, 2)
		};

		Writer = new WaveFileWriter(new IgnoreDisposeStream(MemoryStream), WaveIn.WaveFormat);

		WaveIn.RecordingStopped += OnRecordingStopped;
		WaveIn.DataAvailable += OnDataAvailable;

		var CapsTf2 = WaveInEvent.GetCapabilities(WaveIn.DeviceNumber);
		Console.WriteLine($"Recording from: {CapsTf2.ProductName} (device {WaveIn.DeviceNumber})");

		WaveIn.StartRecording();

		IsRecording = true;
    }

	public void StopRecording()
	{
		WaveIn?.StopRecording();
	}

	private void OnRecordingStopped(object Sender, StoppedEventArgs Args)
	{
		Writer.Dispose();
		Writer = null;
		WaveIn.Dispose();
		WaveIn = null;
		IsRecording = false;
	}

    private void OnDataAvailable(object Sender, WaveInEventArgs Args)
    {
        if (Writer == null) return;

        const int BytesPerFrame = 6;
        float Peak = 0;

        for (int Index = 0; Index + BytesPerFrame - 1 < Args.BytesRecorded; Index += BytesPerFrame)
        {
            Args.Buffer[Index] = Args.Buffer[Index + 3];
            Args.Buffer[Index + 1] = Args.Buffer[Index + 4];
            Args.Buffer[Index + 2] = Args.Buffer[Index + 5];

            int Sample = Args.Buffer[Index] | (Args.Buffer[Index + 1] << 8) | ((sbyte)Args.Buffer[Index + 2] << 16);
            Peak = Math.Max(Peak, Math.Abs(Sample / 8388608f));
        }

        Writer.Write(Args.Buffer, 0, Args.BytesRecorded);
    }

    private WaveOutEvent WaveOut;
	private WaveFileReader Reader;
	private OSampleProviderOne OProvider;
	public bool IsInputValid { get => OProvider != null && !OProvider.IsExpressionValid; }

	public void StartPlayback()
	{
		if (MemoryStream.Length == 0 && Children.Count == 0)
		{
			Console.WriteLine("Nothing to play!");
			return;
		}

		StopPlayback();

		List<IDisposable> Disposables = new();
		List<ISampleProvider> MixInputs = new();

		if (MemoryStream.Length > 0)
		{
			MemoryStream.Seek(0, SeekOrigin.Begin);

			MemoryStream PlaybackStream = new(MemoryStream.ToArray());
			WaveFileReader PlaybackReader = new(PlaybackStream);

			ISampleProvider Provider = PlaybackReader.ToSampleProvider();

			OProvider = new OSampleProviderOne(Provider)
			{
				PlaybackSettings = PlaybackSettings,
			};

			Disposables.Add(PlaybackReader);
			Disposables.Add(PlaybackStream);

			TimeSpan OwnDelay = OwnStartDelay;
			MixInputs.Add(OwnDelay > TimeSpan.Zero ? new OffsetSampleProvider(OProvider) { DelayBy = OwnDelay } : OProvider);
		}

		foreach (var (Child, EmplaceTime) in Children)
		{
			// skip a kid that's being re-recorded from its window, its stream is still being written
			if (Child.MemoryStream.Length == 0 || Child.IsRecording)
			{
				continue;
			}

			Child.MemoryStream.Position = 0;

			MemoryStream ChildStream = new(Child.MemoryStream.ToArray());
			WaveFileReader ChildReader = new(ChildStream);

			ISampleProvider ChildSampleProvider = ChildReader.ToSampleProvider();

			Child.OProvider = new OSampleProviderOne(ChildSampleProvider)
			{
				PlaybackSettings = Child.PlaybackSettings,
			};

			ISampleProvider PositionedProvider = EmplaceTime > TimeSpan.Zero
				? new OffsetSampleProvider(Child.OProvider) { DelayBy = EmplaceTime }
				: Child.OProvider;

			Child.IsPlaying = true;

			Disposables.Add(ChildReader);
			Disposables.Add(ChildStream);
			MixInputs.Add(PositionedProvider);
		}

		if (MixInputs.Count == 0)
		{
			Console.WriteLine("Nothing to play!");
			return;
		}

		ISampleProvider FinalProvider = MixInputs.Count == 1
			? MixInputs[0]
			: new MixingSampleProvider(MixInputs) { ReadFully = false };

		WaveOut = new WaveOutEvent();
		WaveOut.Init(FinalProvider);
		CachedTotalDuration = GetTotalDuration();

		WaveOut.PlaybackStopped += (Sender, Args) =>
		{
			foreach (IDisposable Disposable in Disposables)
			{
				Disposable.Dispose();
			}

			OnPlaybackStopped(Sender, Args);
		};

		WaveOut.Play();
		PlaybackStopwatch.Restart();
		IsPlaying = true;
	}

	public void StopPlayback()
	{
		if (!IsPlaying) return;
		WaveOut?.Stop();
	}

	private void OnPlaybackStopped(object Sender, StoppedEventArgs Args)
	{
		Reader?.Dispose();
		Reader = null;

		WaveOut = null;

		OProvider = null;
		IsPlaying = false;
		CachedTotalDuration = TimeSpan.Zero;
		PlaybackStopwatch.Stop();

		foreach (var (Child, _) in Children)
		{
			Child.OProvider = null;
			Child.IsPlaying = false;
		}
	}

	public void RefreshSettings()
	{
		if (OProvider != null)
		{
			OProvider.PlaybackSettings = PlaybackSettings;
		}

		foreach (var (Child, _) in Children)
		{
			Child.RefreshSettings();
		}
	}

	private TimeSpan GetBaseDuration()
	{
		if (MemoryStream.Length == 0 || IsRecording)
		{
			return TimeSpan.Zero;
		}

		if (!MemoryStream.CanRead)
		{
			return TimeSpan.Zero;
		}

		long CachedPos = MemoryStream.Position;

		MemoryStream.Position = 0;
		using WaveFileReader TempReader = new(new IgnoreDisposeStream(MemoryStream));

		TimeSpan Duration = TempReader.TotalTime;
		MemoryStream.Position = CachedPos;

		return Duration / PlaybackSettings.PlaybackSpeed;
	}

	public TimeSpan GetTotalDuration()
	{
		TimeSpan Latest = GetBaseDuration();

		foreach (var (Child, EmplaceTime) in Children)
		{
			// a kid only plays its trimmed part, starting at EmplaceTime
			var (From, To, _) = Child.GetTrimWindow();
			TimeSpan ChildEnd = EmplaceTime + TimeSpan.FromSeconds(To - From);

			if (ChildEnd > Latest)
			{
				Latest = ChildEnd;
			}
		}

		return Latest;
	}

	private TimeSpan OwnStartDelay => IsChild ? TimeSpan.Zero : TimeSpan.FromSeconds(GetTrimWindow().From);
	private double TrimUnitsPerSecond => PlaybackManager.BitRate * (double) PlaybackSettings.PlaybackSpeed;

	private (double From, double To, double Length) GetTrimWindow()
	{
		double Length = GetBaseDuration().TotalSeconds;
		double From = Math.Clamp(PlaybackSettings.TrimStart / TrimUnitsPerSecond, 0.0, Length);
		double To = PlaybackSettings.TrimEnd < 0 ? Length : Math.Clamp(PlaybackSettings.TrimEnd / TrimUnitsPerSecond, From, Length);
		return (From, To, Length);
	}

	private void SetTrimWindow(double From, double To)
	{
		double Length = GetBaseDuration().TotalSeconds;
		PlaybackSettings.TrimStart = (long) Math.Round(Math.Max(0.0, From) * TrimUnitsPerSecond);
		PlaybackSettings.TrimEnd = To >= Length - 0.0005 ? -1 : (long) Math.Round(To * TrimUnitsPerSecond); // -1 = play to the end
	}

	public event Action<OAudioPlayback, TimeSpan> MergeRequested;
	public event Action<OAudioPlayback> RemoveRequested;
	public event Action PlayRecording;
	public event Action StopPlayingRecording;
	public bool IsChild { get; set; } = false;

	private readonly List<(OAudioPlayback Playback, TimeSpan EmplaceTime)> Children = new();
	public IReadOnlyList<(OAudioPlayback Playback, TimeSpan EmplaceTime)> GetChildren() => Children;

	// whether Target is one of this playback's kids, or one of their kids, and so on
	public bool HasKid(OAudioPlayback Target)
	{
		foreach (var (Child, _) in Children)
		{
			if (Child == Target || Child.HasKid(Target))
			{
				return true;
			}
		}

		return false;
	}

	public void AddChild(OAudioPlayback Child, TimeSpan EmplaceTime)
	{
		PlaybackManager.RemovePlayback(Child);

		// a re-opened kid that gets beamed back up keeps its place (and its row on the timeline)
		int ExistingIndex = Children.FindIndex(C => C.Playback == Child);
		if (ExistingIndex >= 0)
		{
			Children[ExistingIndex] = (Child, EmplaceTime);
			return;
		}

		Children.Add((Child, EmplaceTime));
	}

	public bool RemoveChild(OAudioPlayback Child)
	{
		int RemovedCount = Children.RemoveAll(C => C.Playback == Child);

		if (RemovedCount > 0)
		{
			return true;
		}

		return false;
	}

	private void CreateChildPlayback()
	{
		OAudioPlayback ChildPlayback = new()
		{
			IsChild = true,
		};

		WireChild(ChildPlayback);
		PlaybackManager.AddPlayback(ChildPlayback);
	}

	// points the kid's buttons at this parent, replacing whatever they were hooked up to before
	private void WireChild(OAudioPlayback Child)
	{
		Child.MergeRequested = AddChild;
		Child.RemoveRequested = C => RemoveChild(C);
		Child.PlayRecording = StartPlayback;
		Child.StopPlayingRecording = StopPlayback;
	}

	//////////////////////////////////////////////////////////////////////////////////// 

	public void WritePlayback(BinaryWriter Bw)
	{
		Writer?.Flush();

		byte[] Audio = IsRecording ? [] : MemoryStream.ToArray();
		Bw.Write(Audio.Length);

		if (Audio.Length > 0)
		{
			Bw.Write(Audio);
		}

		var SettingsJson = JsonSerializer.Serialize(PlaybackSettings, PlaybackManager.JsonOptions);
		Bw.Write(SettingsJson);
		Bw.Write(Children.Count);

		foreach (var (Child, EmplaceTime) in Children)
		{
			Child.WritePlayback(Bw);
			Bw.Write(EmplaceTime.Ticks);
		}
	}

	public void ReadPlayback(BinaryReader Br)
	{
		int AudioLength = Br.ReadInt32();

		long BytesLeft = Br.BaseStream.Length - Br.BaseStream.Position;
		if (AudioLength < 0 || AudioLength > BytesLeft)
		{
			throw new InvalidDataException($"its wave should be {AudioLength} bytes but only {BytesLeft} are left, it's probably cut short");
		}

		if (AudioLength > 0)
		{
			byte[] Audio = Br.ReadBytes(AudioLength);
			MemoryStream.Write(Audio, 0, AudioLength);
			MemoryStream.Position = 0;
		}

		string SettingsJson = Br.ReadString();
		PlaybackSettings = JsonSerializer.Deserialize<FPlaybackSettings>(SettingsJson, PlaybackManager.JsonOptions) ?? new FPlaybackSettings();

		int ChildCount = Br.ReadInt32();
		for (int ChildIndex = 0; ChildIndex < ChildCount; ++ChildIndex)
		{
			OAudioPlayback Child = new()
			{
				IsChild = true,
			};
			Child.ReadPlayback(Br);
			TimeSpan EmplaceTime = new(Br.ReadInt64());
			Children.Add((Child, EmplaceTime));
		}
	}

	public void ExportProvider(out List<ISampleProvider> Export, bool IsRoot = true)
	{
		Export = new();

		MemoryStream.Seek(0, SeekOrigin.Begin);

		MemoryStream CopyStream = new(MemoryStream.ToArray());
		WaveFileReader Reader = new(CopyStream);
		ISampleProvider BaseProvider = Reader.ToSampleProvider();

		OSampleProviderOne Provider = new(BaseProvider)
		{
			PlaybackSettings = PlaybackSettings,
		};

		// only the playback being exported gets this; a kid's start trim is already in its EmplaceTime
		TimeSpan OwnDelay = IsRoot ? OwnStartDelay : TimeSpan.Zero;
		Export.Add(OwnDelay > TimeSpan.Zero ? new OffsetSampleProvider(Provider) { DelayBy = OwnDelay } : Provider);

		foreach (var (Child, EmplaceTime) in Children)
		{
			if (!Child.HasRecording)
			{
				continue;
			}

			Child.ExportProvider(out var ChildExports, IsRoot: false);

			foreach (var ChildExport in ChildExports)
			{
				ISampleProvider PositionedChild = EmplaceTime > TimeSpan.Zero ? new OffsetSampleProvider(ChildExport) { DelayBy = EmplaceTime } : ChildExport;
				Export.Add(PositionedChild);
			}
		}
	}

	private void TrimStream(long Start, long End)
	{
		if (MemoryStream.Length == 0) return;

		MemoryStream.Position = 0;
		WaveFormat Format;
		long AudioDataLength;

		using (WaveFileReader TempReader = new(new IgnoreDisposeStream(MemoryStream)))
		{
			Format = TempReader.WaveFormat;
			AudioDataLength = TempReader.Length;
		}

		long AudioStart = (long) ((double) Start / PlaybackManager.BitRate * PlaybackSettings.PlaybackSpeed * Format.AverageBytesPerSecond);
		long AudioEnd = (long) ((double) End / PlaybackManager.BitRate * PlaybackSettings.PlaybackSpeed * Format.AverageBytesPerSecond);

		AudioStart = Math.Clamp(AudioStart, 0, AudioDataLength);
		AudioEnd = Math.Clamp(AudioEnd, 0, AudioDataLength);

		AudioStart = (AudioStart / Format.BlockAlign) * Format.BlockAlign;
		AudioEnd = (AudioEnd / Format.BlockAlign) * Format.BlockAlign;

		if (AudioStart >= AudioEnd)
		{
			MemoryStream.Position = 0;
			MemoryStream.SetLength(0);
			return;
		}

		byte[] AudioBuffer;
		int BytesRead;

		MemoryStream.Position = 0;
		using (WaveFileReader Reader = new(new IgnoreDisposeStream(MemoryStream)))
		{
			Reader.Position = AudioStart;
			AudioBuffer = new byte[AudioEnd - AudioStart];
			BytesRead = Reader.Read(AudioBuffer, 0, AudioBuffer.Length);
		}

		using MemoryStream TempStream = new();
		using (WaveFileWriter TrimWriter = new(new IgnoreDisposeStream(TempStream), Format))
		{
			TrimWriter.Write(AudioBuffer, 0, BytesRead);
			TrimWriter.Flush();
		}

		MemoryStream.Position = 0;
		MemoryStream.SetLength(0);
		TempStream.Position = 0;
		TempStream.CopyTo(MemoryStream);
		MemoryStream.Position = 0;
	}

	public void Trim(long Start, long End)
	{
		StopRecording();
		StopPlayback();

		if (MemoryStream.Length == 0 && Children.Count == 0) return;
		if (Start < 0) Start = 0;
		if (End <= Start) return;

		TimeSpan WallClockStart = TimeSpan.FromSeconds(Start / (double)PlaybackManager.BitRate);
		TimeSpan WallClockEnd = TimeSpan.FromSeconds(End / (double)PlaybackManager.BitRate);

		TrimStream(Start, End);
		TrimKids(WallClockStart, WallClockEnd);
	}

	// Keeps the parts of the kids that fall inside Start..End of this playback's timeline and shifts them so
	// Start becomes 0. Kids get the same non-destructive trim as dragging their edges, so it matches what
	// they actually play (even if they were already trimmed) and they can still be pulled back out after.
	private void TrimKids(TimeSpan Start, TimeSpan End)
	{
		for (int ChildIndex = Children.Count - 1; ChildIndex >= 0; --ChildIndex)
		{
			var (Child, EmplaceTime) = Children[ChildIndex];

			if (Child.IsRecording)
			{
				continue; // being re-recorded from its window, leave it alone
			}

			// a kid plays From..To of its recording, starting at EmplaceTime
			var (From, To, _) = Child.GetTrimWindow();
			TimeSpan ChildWallEnd = EmplaceTime + TimeSpan.FromSeconds(To - From);

			if (ChildWallEnd <= Start || EmplaceTime >= End)
			{
				Children.RemoveAt(ChildIndex);
				continue;
			}

			TimeSpan CutFront = Start > EmplaceTime ? Start - EmplaceTime : TimeSpan.Zero; // how much of the kid falls before Start
			TimeSpan KeepUntil = (End < ChildWallEnd ? End : ChildWallEnd) - EmplaceTime; // end of what's kept, from the kid's start

			Child.SetTrimWindow(From + CutFront.TotalSeconds, From + KeepUntil.TotalSeconds);
			Child.TrimKids(CutFront, End - EmplaceTime); // its own kids hang off its start, which just moved by CutFront
			Children[ChildIndex] = (Child, EmplaceTime > Start ? EmplaceTime - Start : TimeSpan.Zero);
		}
	}

	public void Loop(int Times)
	{
		if (Times <= 1) return;

		StopRecording();
		StopPlayback();

		if (MemoryStream.Length == 0) return;

		MemoryStream.Position = 0;

		WaveFormat Format;
		byte[] AudioData;

		using (WaveFileReader TempReader = new(new IgnoreDisposeStream(MemoryStream)))
		{
			Format = TempReader.WaveFormat;

			AudioData = new byte[TempReader.Length];
			int BytesRead = TempReader.Read(AudioData, 0, AudioData.Length);
			Array.Resize(ref AudioData, BytesRead);
		}

		using MemoryStream TempStream = new();
		using (WaveFileWriter LoopWriter = new(new IgnoreDisposeStream(TempStream), Format))
		{
			for (int I = 0; I < Times; I++)
			{
				LoopWriter.Write(AudioData, 0, AudioData.Length);
			}

			LoopWriter.Flush();
		}

		MemoryStream.Position = 0;
		MemoryStream.SetLength(0);

		TempStream.Position = 0;
		TempStream.CopyTo(MemoryStream);

		MemoryStream.Position = 0;
	}

	////////////////////////////////////////////////////////////////////////////////////

	public void LoadFromMp3(string FilePath)
	{
		StopRecording();
		StopPlayback();

		MemoryStream.Position = 0;
		MemoryStream.SetLength(0);

		using AudioFileReader AudioFile = new(FilePath);

		ISampleProvider Resampled = AudioFile.WaveFormat.SampleRate != PlaybackManager.SampleRate ? new WdlResamplingSampleProvider(AudioFile, PlaybackManager.SampleRate) : AudioFile;
		ISampleProvider Final = AudioFile.WaveFormat.Channels == 1 && PlaybackManager.Channels == 2 ? new MonoToStereoSampleProvider(Resampled) : Resampled;

		using MemoryStream TempStream = new();
		using (WaveFileWriter WavWriter = new(new IgnoreDisposeStream(TempStream), Final.WaveFormat))
		{
			float[] Buffer = new float[8192];
			int SamplesRead;
			while ((SamplesRead = Final.Read(Buffer, 0, Buffer.Length)) > 0)
			{
				WavWriter.WriteSamples(Buffer, 0, SamplesRead);
			}
			WavWriter.Flush();
		}

		TempStream.Position = 0;
		TempStream.CopyTo(MemoryStream);
		MemoryStream.Position = 0;
	}
}