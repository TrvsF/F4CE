using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace F4CE.Objects;

internal partial class OAudioPlayback
{
	private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };
	private const int SerializationVersion = 1;

	public static void SaveListToFile(IReadOnlyList<OAudioPlayback> Playbacks, string FilePath)
	{
		string? Directory = Path.GetDirectoryName(FilePath);
		if (!string.IsNullOrEmpty(Directory))
		{
			System.IO.Directory.CreateDirectory(Directory);
		}

		using FileStream Fs = new(FilePath, FileMode.Create, FileAccess.Write);
		using BinaryWriter Bw = new(Fs);

		Bw.Write(SerializationVersion);
		Bw.Write(Playbacks.Count);

		foreach (OAudioPlayback Playback in Playbacks)
		{
			WritePlayback(Bw, Playback);
		}
	}

	public static List<OAudioPlayback> LoadListFromFile(string FilePath)
	{
		if (!File.Exists(FilePath))
		{
			return [];
		}

		using FileStream FileStream = new(FilePath, FileMode.Open, FileAccess.Read);
		using BinaryReader BinaryReader = new(FileStream);

		int Version = BinaryReader.ReadInt32();
		if (Version != SerializationVersion)
		{
			throw new InvalidDataException(
				$"Save file version {Version} is not supported by this build (expected {SerializationVersion}).");
		}

		int ReadCount = BinaryReader.ReadInt32();
		List<OAudioPlayback> Result = new(ReadCount);

		for (int ReadIndex = 0; ReadIndex < ReadCount; ++ReadIndex)
		{
			Result.Add(ReadPlayback(BinaryReader, IsChild: false));
		}

		return Result;
	}

	private static void WritePlayback(BinaryWriter Bw, OAudioPlayback Playback)
	{
		Playback.Writer?.Flush();

		byte[] Audio = Playback.IsRecording ? [] : Playback.MemoryStream.ToArray();
		Bw.Write(Audio.Length);

		if (Audio.Length > 0)
		{
			Bw.Write(Audio);
		}

		var SettingsJson = JsonSerializer.Serialize(Playback.PlaybackSettings, JsonOptions); 
		Bw.Write(SettingsJson);

		IReadOnlyList<(OAudioPlayback Playback, TimeSpan EmplaceTime)> Kids = Playback.GetChildren();
		Bw.Write(Kids.Count);

		foreach (var (Child, EmplaceTime) in Kids)
		{
			WritePlayback(Bw, Child);
			Bw.Write(EmplaceTime.Ticks);
		}
	}

	private static OAudioPlayback ReadPlayback(BinaryReader Br, bool IsChild)
	{
		OAudioPlayback Playback = new() { IsChild = IsChild };

		int AudioLength = Br.ReadInt32();
		if (AudioLength > 0)
		{
			byte[] Audio = Br.ReadBytes(AudioLength);
			Playback.MemoryStream.Write(Audio, 0, AudioLength);
			Playback.MemoryStream.Position = 0;
		}

		string SettingsJson = Br.ReadString();
		Playback.PlaybackSettings = JsonSerializer.Deserialize<FPlaybackSettings>(SettingsJson, JsonOptions) ?? new FPlaybackSettings();

		int ChildCount = Br.ReadInt32();
		for (int ChildIndex = 0; ChildIndex < ChildCount; ++ChildIndex)
		{
			OAudioPlayback Child = ReadPlayback(Br, IsChild: true);
			TimeSpan EmplaceTime = new(Br.ReadInt64());
			Playback.Children.Add((Child, EmplaceTime));
		}

		return Playback;
	}
}
