using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace F4CE.Objects;

internal partial class OAudioPlayback : IDisposable
{
	private bool Disposed = false;

	public OAudioPlayback() { }

	public OAudioPlayback(OAudioPlayback Source)
	{
		PlaybackSettings = Source.PlaybackSettings;

		byte[] AudioData = Source.MemoryStream.ToArray();

		if (AudioData.Length > 0)
		{
			MemoryStream.Write(AudioData, 0, AudioData.Length);
			MemoryStream.Position = 0;
		}

		foreach (var (Child, EmplaceTime) in Source.Children)
		{
			Children.Add((new OAudioPlayback(Child), EmplaceTime));
		}
	}

	public OAudioPlayback DeepClone()
	{
		ObjectDisposedException.ThrowIf(Disposed, this);

		OAudioPlayback Clone = new(this);
		return Clone;
	}

	// A copy that's fully separate from the original: its own audio, settings and kids, so trimming or
	// changing the copy never touches the original (the copy constructor above shares the settings).
	public OAudioPlayback Duplicate()
	{
		OAudioPlayback Copy = new()
		{
			IsChild = IsChild,
			PlaybackSettings = PlaybackSettings with { },
		};

		byte[] AudioData = MemoryStream.ToArray();
		if (AudioData.Length > 0)
		{
			Copy.MemoryStream.Write(AudioData, 0, AudioData.Length);
			Copy.MemoryStream.Position = 0;
		}

		foreach (var (Child, EmplaceTime) in Children)
		{
			Copy.Children.Add((Child.Duplicate(), EmplaceTime));
		}

		return Copy;
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool Disposing)
	{
		if (Disposed)
		{
			return;
		}

		if (Disposing)
		{
			StopPlayback();

			WaveIn?.StopRecording();
			WaveIn?.Dispose();
			WaveIn = null;

			Writer?.Dispose();
			Writer = null;

			Reader?.Dispose();
			Reader = null;

			OProvider = null;

			MemoryStream.Dispose();
		}

		Disposed = true;
	}

	~OAudioPlayback()
	{
		Dispose(false);
	}

	public override string ToString()
	{
		return ImGuiD.ToString();
	}
}