using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace F4CE.Objects;

internal class OFrequencyShiftSampleProvider : ISampleProvider
{
	private readonly ISampleProvider Source;

	private float Phase;
	private float PanPhase;

	// 0.25 = slow movement
	// 1.0 = faster movement
	public float PanSpeed { get; set; } = 2f;
	public long Duration { get; set; }

	public WaveFormat WaveFormat => Source.WaveFormat;

	public OFrequencyShiftSampleProvider(ISampleProvider InSource)
	{
		Source = InSource;

		if (WaveFormat.Channels != 2)
		{
			throw new InvalidOperationException("Stereo output required");
		}
	}

	public int Read(float[] Buffer, int Offset, int Count)
	{
		int Read = Source.Read(Buffer, Offset, Count);

		float SampleRate = WaveFormat.SampleRate;

		// Stereo interleaved:
		// Left  = index 0
		// Right = index 1
		for (int ReadIndex = 0; ReadIndex < Read; ReadIndex += 2)
		{
			float Time = Phase * 4;

			float Frequency = MathF.Sin(Time * 0.3f) * 960f;

			float Sample =
				MathF.Sin(2f * MathF.PI * Frequency * Time) * 0.5f +
				MathF.Sin(2f * MathF.PI * Frequency * 2f * Time) * 0.25f +
				MathF.Sin(2f * MathF.PI * Frequency * 4f * Time) * 0.1f;

			Sample *= 0.2f;
			float Pan = MathF.Sin(2f * MathF.PI * PanSpeed * PanPhase);

			float LeftGain = (1f - Pan) * 0.5f;
			float RightGain = (1f + Pan) * 0.5f;

			Buffer[Offset + ReadIndex] = Sample * LeftGain;
			Buffer[Offset + ReadIndex + 1] = Sample * RightGain;

			Phase += 1f / SampleRate;
			PanPhase += 1f / SampleRate;
		}

		return Read;
	}
}

internal class OAudioPlayback
{
	public MemoryStream Stream { get; } = new();

	public bool IsRecording { get; private set; } = false;
	public bool IsPlaying { get; private set; } = false;
	public long Length { get => Stream.Length; }

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

		Stream.Position = 0;
		Stream.SetLength(0);

		TempStream.Position = 0;
		TempStream.CopyTo(Stream);

		Stream.Position = 0;
	}

	public void StartRecording()
	{
		StopPlayback();

		Stream.Position = 0;
		Stream.SetLength(0);

		WaveIn = new WaveInEvent
		{
			DeviceNumber = 0,
			WaveFormat = new WaveFormat(44100, 2)
		};

		Writer = new WaveFileWriter(Stream, WaveIn.WaveFormat);

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
		Writer.Flush();
		WaveIn.Dispose();

		IsRecording = false;
	}

	private void OnDataAvailable(object Sender, WaveInEventArgs Args)
	{
		if (Writer == null) return;
		Writer.Write(Args.Buffer, 0, Args.BytesRecorded);
	}

	private WaveOutEvent WaveOut;
	private WaveFileReader Reader;

	public void PlayRecording()
	{
		if (Stream.Length == 0)
		{
			Console.WriteLine("Nothing to play!");
			return;
		}

		StopPlayback();

		Stream.Seek(0, SeekOrigin.Begin);

		var Stream1 = new MemoryStream(Stream.ToArray());
		var Stream2 = new MemoryStream(Stream.ToArray());

		var Reader1 = new WaveFileReader(Stream1);
		var Reader2 = new WaveFileReader(Stream2);

		ISampleProvider LeftProvider = new MonoToStereoSampleProvider(Reader1.ToSampleProvider())
		{
			LeftVolume = 1f,
			RightVolume = 0f,
		};

		ISampleProvider RightProvider = new MonoToStereoSampleProvider(Reader2.ToSampleProvider())
		{
			LeftVolume = 0f,
			RightVolume = 1f,
		};

		var Mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Reader1.WaveFormat.SampleRate, 2))
		{
			ReadFully = true
		};

		Mixer.AddMixerInput(LeftProvider);
		Mixer.AddMixerInput(RightProvider);

		WaveOut = new WaveOutEvent();

		WaveOut.Init(Mixer);

		WaveOut.PlaybackStopped += OnPlaybackStopped;

		WaveOut.Play();

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

		WaveOut?.Dispose();
		WaveOut = null;

		IsPlaying = false;
	}

	public void PlaySWave()
	{
		if (Stream.Length == 0)
		{
			Console.WriteLine("Nothing to play!");
			return;
		}

		StopPlayback();

		Stream.Position = 0;

		MemoryStream PlaybackStream = new(Stream.ToArray());
		WaveFileReader PlaybackReader = new(PlaybackStream);

		ISampleProvider Provider = PlaybackReader.ToSampleProvider();

		Provider = new OFrequencyShiftSampleProvider(Provider)
		{
			Duration = Stream.Length,
		};

		WaveOut = new WaveOutEvent();

		WaveOut.Init(Provider);

		WaveOut.PlaybackStopped += (Sender, Args) =>
		{
			PlaybackReader.Dispose();
			PlaybackStream.Dispose();

			OnPlaybackStopped(Sender, Args);
		};

		WaveOut.Play();

		IsPlaying = true;
	}

	public static OAudioPlayback CombinePlaybacks(IEnumerable<OAudioPlayback> InPlaybacks)
	{
		List<ISampleProvider> Providers = new();

		foreach (var Playback in InPlaybacks)
		{
			if (Playback.Stream.Length == 0)
			{
				continue;
			}

			Playback.Stream.Position = 0;

			WaveFileReader Reader = new(Playback.Stream);
			Providers.Add(Reader.ToSampleProvider());
		}

		OAudioPlayback CombinedPlayback = new();

		if (Providers.Count == 0)
		{
			return CombinedPlayback;
		}

		ConcatenatingSampleProvider ConcatenatedProvider = new(Providers);
		WaveFileWriter.WriteWavFileToStream(CombinedPlayback.Stream, ConcatenatedProvider.ToWaveProvider());

		CombinedPlayback.Stream.Position = 0;

		return CombinedPlayback;
	}

	public float[] GetWaveform(int SampleCount = 512, float Sensitivity = 1.0f)
	{
		if (Stream.Length == 0)
		{
			return [];
		}

		long OldPos = Stream.Position;
		Stream.Position = 0;

		using WaveFileReader Reader = new(Stream);

		ISampleProvider Provider = Reader.ToSampleProvider();

		List<float> Samples = new();
		float[] Buffer = new float[1024];

		int Read;
		while ((Read = Provider.Read(Buffer, 0, Buffer.Length)) > 0)
		{
			for (int ReadIndex = 0; ReadIndex < Read; ReadIndex++)
			{
				float Sample = Buffer[ReadIndex] * Sensitivity;

				Sample = Math.Clamp(Sample, -1f, 1f);

				Samples.Add(Sample);
			}
		}

		Stream.Position = OldPos;

		if (Samples.Count == 0)
		{
			return [];
		}

		float[] Result = new float[SampleCount];

		float Stride = (float)Samples.Count / SampleCount;

		for (int i = 0; i < SampleCount; i++)
		{
			int Start = (int)(i * Stride);
			int End = Math.Min((int)((i + 1) * Stride), Samples.Count);

			float Peak = 0f;

			for (int j = Start; j < End; j++)
			{
				float Abs = Math.Abs(Samples[j]);

				if (Abs > Peak)
				{
					Peak = Abs;
				}
			}

			Result[i] = Peak;
		}

		return Result;
	}
}
