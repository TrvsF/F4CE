using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace F4CE.Objects;

internal partial class OAudioPlayback
{
	public MemoryStream MemoryStream { get; } = new();

	public FPlaybackSettings PlaybackSettings { get; private set; } = new();
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

		WaveFormat Format = new(44100, 2);

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
			WaveFormat = new WaveFormat(44100, 2)
		};

		Writer = new WaveFileWriter(new IgnoreDisposeStream(MemoryStream), WaveIn.WaveFormat);

		WaveIn.RecordingStopped += OnRecordingStopped;
		WaveIn.DataAvailable += OnDataAvailable;

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
				BaseDuration = MemoryStream.Length,
				PlaybackSettings = PlaybackSettings,
			};

			Disposables.Add(PlaybackReader);
			Disposables.Add(PlaybackStream);
			MixInputs.Add(OProvider);
		}

		foreach (var (Child, EmplaceTime) in Children)
		{
			if (Child.MemoryStream.Length == 0)
			{
				continue;
			}

			Child.MemoryStream.Position = 0;

			MemoryStream ChildStream = new(Child.MemoryStream.ToArray());
			WaveFileReader ChildReader = new(ChildStream);

			ISampleProvider ChildSampleProvider = ChildReader.ToSampleProvider();

			Child.OProvider = new OSampleProviderOne(ChildSampleProvider)
			{
				BaseDuration = Child.MemoryStream.Length,
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

		WaveOut = null; // already disposed by the PlaybackStopped

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
		if (MemoryStream.Length == 0)
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
			TimeSpan ChildEnd = EmplaceTime + Child.GetBaseDuration();

			if (ChildEnd > Latest)
			{
				Latest = ChildEnd;
			}
		}

		return Latest;
	}

	public event Action<OAudioPlayback, TimeSpan> MergeRequested;
	public bool IsChild { get; init; } = false;

	private readonly List<(OAudioPlayback Playback, TimeSpan EmplaceTime)> Children = new();
	public IReadOnlyList<(OAudioPlayback Playback, TimeSpan EmplaceTime)> GetChildren() => Children;

	public void RequestAddition(OAudioPlayback Child, TimeSpan EmplaceTime)
	{
		Window.RemovePlayback(Child);

		Children.RemoveAll(C => C.Playback == Child);
		Children.Add((Child, EmplaceTime));
	}

	public bool RemoveAddition(OAudioPlayback Child)
	{
		int RemovedCount = Children.RemoveAll(C => C.Playback == Child);

		if (RemovedCount > 0)
		{
			return true;
		}

		return false;
	}

	public static void SaveAllPlaybacksToFile()
	{
		if (Window.ActivePlaybacks.Count == 0)
		{
			return;
		}

		string DesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
		string Path = System.IO.Path.Combine(DesktopPath, "F4CE.wav");

		int SaveCounter = 1337;
		while (File.Exists(Path))
		{
			Path = System.IO.Path.Combine(DesktopPath, $"F4CE-{SaveCounter}.wav");
			++SaveCounter;
		}

		List<ISampleProvider> RenderedProviders = new();

		foreach (var Playback in Window.ActivePlaybacks)
		{
			if (Playback.HasRecording)
			{
				MemoryStream CopyStream = new(Playback.MemoryStream.ToArray());
				WaveFileReader Reader = new(CopyStream);
				ISampleProvider BaseProvider = Reader.ToSampleProvider();

				OSampleProviderOne Shifted = new(BaseProvider)
				{
					BaseDuration = Playback.MemoryStream.Length,
					PlaybackSettings = Playback.PlaybackSettings,
				};

				RenderedProviders.Add(Shifted);
			}

			foreach (var (Child, EmplaceTime) in Playback.GetChildren())
			{
				if (!Child.HasRecording)
				{
					continue;
				}

				MemoryStream ChildCopyStream = new(Child.MemoryStream.ToArray());
				WaveFileReader ChildReader = new(ChildCopyStream);
				ISampleProvider ChildBaseProvider = ChildReader.ToSampleProvider();

				OSampleProviderOne ChildShifted = new(ChildBaseProvider)
				{
					BaseDuration = Child.MemoryStream.Length,
					PlaybackSettings = Child.PlaybackSettings,
				};

				ISampleProvider PositionedChild = EmplaceTime > TimeSpan.Zero
					? new OffsetSampleProvider(ChildShifted) { DelayBy = EmplaceTime }
					: ChildShifted;

				RenderedProviders.Add(PositionedChild);
			}
		}

		if (RenderedProviders.Count == 0)
		{
			return;
		}

		MixingSampleProvider Mixer = new(RenderedProviders);
		Mixer.ReadFully = false;

		WaveFormat Format = Mixer.WaveFormat;
		using WaveFileWriter Writer = new(Path, Format);

		float[] Buffer = new float[1024];
		int Read;

		while ((Read = Mixer.Read(Buffer, 0, Buffer.Length)) > 0)
		{
			Writer.WriteSamples(Buffer, 0, Read);
		}

		Writer.Flush();
	}
}