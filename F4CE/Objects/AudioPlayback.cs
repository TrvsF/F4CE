using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace F4CE.Objects;

class OAudioPlayback
{
	private WaveInEvent WaveIn;
	private WaveFileWriter Writer;

	private WaveOutEvent WaveOut;
	private WaveFileReader Reader;

	private MemoryStream Stream;

	public bool IsRecording { get; private set; } = false;
	public bool IsPlaying { get; private set; } = false;

	public void StartRecording()
	{
		StopPlayback();

		Stream = new MemoryStream();

		WaveIn = new WaveInEvent
		{
			DeviceNumber = 0,
			WaveFormat = new WaveFormat(44100, 1)
		};

		Writer = new WaveFileWriter(Stream, WaveIn.WaveFormat);

		WaveIn.DataAvailable += OnDataAvailable;
		WaveIn.RecordingStopped += OnRecordingStopped;

		WaveIn.StartRecording();

		IsRecording = true;
	}

	private void OnDataAvailable(object Sender, WaveInEventArgs Args)
	{
		if (Writer == null) return;
		Writer.Write(Args.Buffer, 0, Args.BytesRecorded);
	}

	private void OnRecordingStopped(object Sender, StoppedEventArgs Args)
	{
		Writer.Flush();
		WaveIn.Dispose();
		Stream.Seek(0, SeekOrigin.Begin);

		IsRecording = false;
	}

	public void StopRecording()
	{
		WaveIn?.StopRecording();
	}

	public void PlayRecording()
	{
		if (Stream == null || Stream.Length == 0)
		{
			Console.WriteLine("Generic Playback Error!");
			return;
		}

		StopPlayback();

		Reader = new WaveFileReader(Stream);

		WaveOut = new WaveOutEvent();
		WaveOut.Init(Reader);

		WaveOut.PlaybackStopped += OnPlaybackStopped;
		WaveOut.Play();

		IsPlaying = true;
	}

	private void OnPlaybackStopped(object Sender, StoppedEventArgs Args)
	{
		Reader?.Dispose();
		Reader = null;

		WaveOut?.Dispose();
		WaveOut = null;

		IsPlaying = false;
	}

	public void StopPlayback()
	{
		if (!IsPlaying) return;
		WaveOut?.Stop();
	}
}
