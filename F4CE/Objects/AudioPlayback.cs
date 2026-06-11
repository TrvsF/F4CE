using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.IO;

namespace F4CE.Objects;

internal partial class OAudioPlayback
{
	public MemoryStream MemoryStream { get; } = new();

	public bool IsRecording { get; private set; } = false;
	public bool IsPlaying { get; private set; } = false;
	public long Length { get => MemoryStream.Length; }

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

		Writer = new WaveFileWriter(MemoryStream, WaveIn.WaveFormat);

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
	private OSampleProviderOne OProvider;
	public bool IsInputValid { get => OProvider != null && !OProvider.IsExpressionValid; }

	public void SetProviderSettings()
	{
		if (OProvider == null)
		{
			return;
		}

		OProvider.Raw = Raw;
		OProvider.Rs = Rs;
		OProvider.PanSpeed = PanSpeed;
		OProvider.TransposeSemitones = Transpose;
		OProvider.WaveExpression = WaveExpression;
		OProvider.PlaybackSpeed = PlaybackSpeed;
		OProvider.Loudness = Loudness;
		OProvider.PanBaseVolume = PanBaseVolume;
	}

	public void PlayRecording()
	{
		if (MemoryStream.Length == 0)
		{
			Console.WriteLine("Nothing to play!");
			return;
		}

		StopPlayback();

		MemoryStream.Seek(0, SeekOrigin.Begin);

		MemoryStream PlaybackStream = new(MemoryStream.ToArray());
		WaveFileReader PlaybackReader = new(PlaybackStream);

		ISampleProvider Provider = PlaybackReader.ToSampleProvider();

		OProvider = new OSampleProviderOne(Provider)
		{
			Duration = MemoryStream.Length,
		};
		SetProviderSettings();

		WaveOut = new WaveOutEvent();

		WaveOut.Init(OProvider);

		WaveOut.PlaybackStopped += (Sender, Args) =>
		{
			PlaybackReader.Dispose();
			PlaybackStream.Dispose();

			OnPlaybackStopped(Sender, Args);
		};

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

		OProvider = null;
		IsPlaying = false;
	}

	public static OAudioPlayback CombinePlaybacks(IEnumerable<OAudioPlayback> InPlaybacks)
	{
		List<ISampleProvider> Providers = new();

		foreach (var Playback in InPlaybacks)
		{
			if (Playback.MemoryStream.Length == 0)
			{
				continue;
			}

			Playback.MemoryStream.Position = 0;

			WaveFileReader Reader = new(Playback.MemoryStream);
			Providers.Add(Reader.ToSampleProvider());
		}

		OAudioPlayback CombinedPlayback = new();

		if (Providers.Count == 0)
		{
			return CombinedPlayback;
		}

		ConcatenatingSampleProvider ConcatenatedProvider = new(Providers);
		WaveFileWriter.WriteWavFileToStream(CombinedPlayback.MemoryStream, ConcatenatedProvider.ToWaveProvider());

		CombinedPlayback.MemoryStream.Position = 0;

		return CombinedPlayback;
	}

	public float[] GetWaveform(int SampleCount = 512, float Sensitivity = 1.0f)
	{
		if (MemoryStream.Length == 0)
		{
			return [];
		}

		long OldPos = MemoryStream.Position;
		MemoryStream.Position = 0;

		using WaveFileReader Reader = new(MemoryStream);

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

		MemoryStream.Position = OldPos;

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
