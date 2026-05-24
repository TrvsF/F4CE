using ImGuiNET;
using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection.PortableExecutable;

namespace F4CE.Objects;

internal class OAudioPlayback
{
	public MemoryStream Stream { get; } = new();

	public bool IsRecording { get; private set; } = false;
	public bool IsPlaying { get; private set; } = false;
	public long Length { get => Stream.Length; }

	private WaveInEvent WaveIn;
	private WaveFileWriter Writer;

	public void StartRecording()
	{
		StopPlayback();

		Stream.Position = 0;
		Stream.SetLength(0);

		WaveIn = new WaveInEvent
		{
			DeviceNumber = 0,
			WaveFormat = new WaveFormat(44100, 1)
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
		Reader = new WaveFileReader(Stream);

		WaveOut = new WaveOutEvent();
		WaveOut.Init(Reader);

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
