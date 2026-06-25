using ImGuiNET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace F4CE.Objects;

public record FPlaybackSettings
{
	public string WaveExpression = "f*t";
	public bool Raw = false;
	public long TrimStart = 0;
	public long TrimEnd = -1;
	public float TransposeSemitones = 0f;
	public float PlaybackSpeed = 1f;
	public float SilenceSeconds = 30f;
	public float PanBaseVolume = 0f;
	public float PanSpeed = 0f;
	public float Loudness = 1f;
	public float LeftLoundness = 1f;
	public float RightLoundness = 1f;
	public int Rs = 3;
}

internal partial class OAudioPlayback
{
	public readonly Guid ImGuiD = Guid.NewGuid();

	public readonly string Mp3ImportFolder = "C:/";
	public List<string> SMp3FilePaths = new();
	public int SMp3SelectedIndex = -1;
	public bool SMp3FolderScanned = false;

	int SelectedIndex = -1;

	const float PixelsPerSecond = 20f;
	const float MainHeight = 40f;
	const float ChildHeight = 28f;
	const float ChildGap = 4f;

	public void DrawBlock()
	{
		ImGui.PushID(ImGuiD.ToString());

		// TODO : wrap me!
		var DrawList = ImGui.GetWindowDrawList();
		var TimelineOrigin = ImGui.GetCursorScreenPos();
		var MainColour = ImGui.GetColorU32(new Vector4(0.20f, 0.55f, 0.95f, 1.0f));

		if (IsChild)
		{
			ImGui.Begin($"kidnamed{ImGuiD}");
		}

		if (!HasRecording)
		{
			if (!IsRecording)
			{
				if (ImGui.Button("Start Recording", new Vector2(160, 20)))
				{
					StartRecording();
				}
				
				ImGui.SliderFloat("Silence Length (Seconds)", ref PlaybackSettings.SilenceSeconds, 0f, 120f);
				if (ImGui.Button("Create Silent Playback", new Vector2(160, 20)))
				{
					SetSilence(TimeSpan.FromSeconds(PlaybackSettings.SilenceSeconds));
					PlaybackSettings.WaveExpression = "sin(t*PI*100)";
				}

				bool Refresh = ImGui.Button("Refresh##Mp3Refresh");

				if (!SMp3FolderScanned || Refresh)
				{
					SMp3FilePaths = Directory.Exists(Mp3ImportFolder)
						? new List<string>(Directory.GetFiles(Mp3ImportFolder, "*.mp3", SearchOption.TopDirectoryOnly))
						: new List<string>();

					SMp3SelectedIndex = -1;
					SMp3FolderScanned = true;
				}

				ImGui.SameLine();

				string PreviewLabel = SMp3SelectedIndex >= 0 && SMp3SelectedIndex < SMp3FilePaths.Count
					? Path.GetFileNameWithoutExtension(SMp3FilePaths[SMp3SelectedIndex])
					: SMp3FilePaths.Count > 0 ? "Select MP3..." : "(no files found)";

				if (ImGui.BeginCombo("##Mp3ImportCombo", PreviewLabel))
				{
					for (int Mp3Index = 0; Mp3Index < SMp3FilePaths.Count; Mp3Index++)
					{
						bool IsSelected = SMp3SelectedIndex == Mp3Index;
						string FileName = Path.GetFileNameWithoutExtension(SMp3FilePaths[Mp3Index]);

						if (ImGui.Selectable(FileName, IsSelected))
						{
							SMp3SelectedIndex = Mp3Index;
							LoadFromMp3(SMp3FilePaths[Mp3Index]);
							Console.WriteLine($"Imported MP3: {FileName}");
						}

						if (IsSelected)
						{
							ImGui.SetItemDefaultFocus();
						}
					}

					ImGui.EndCombo();
				}
			}
			else
			{
				if (ImGui.Button("Stop Recording", new Vector2(160, 20)))
				{
					StopRecording();
				}

				TimelineOrigin = ImGui.GetCursorScreenPos();
				var TotalWidth = PixelsPerSecond * (MemoryStream.Length / PlaybackManager.BitRate);
				DrawList.AddRectFilled(TimelineOrigin, TimelineOrigin + new Vector2(TotalWidth, MainHeight), MainColour);
			}
		}
		else
		{
			if (ImGui.Button("Clear"))
			{
				MemoryStream.SetLength(0);
			}

			ImGui.Text($"{Children.Count}kidz");
			ImGui.SameLine();

			if (!IsPlaying)
			{
				if (ImGui.Button($"Play Recording", new Vector2(120, 20)))
				{
					StartPlayback();
				}
			}
			else
			{
				if (ImGui.Button($"Stop", new Vector2(120, 20)))
				{
					StopPlayback();
				}
			}
			ImGui.SameLine();
			ImGui.Checkbox("Raw", ref PlaybackSettings.Raw);
			ImGui.SameLine();
			ImGui.SetNextItemWidth(80);
			ImGui.SliderFloat("PanSpeed", ref PlaybackSettings.PanSpeed, 0f, 20f);
			ImGui.SameLine();
			ImGui.SetNextItemWidth(80);
			ImGui.SliderFloat("PlaybackSpeed", ref PlaybackSettings.PlaybackSpeed, 0.1f, 5f);
			ImGui.SameLine();
			ImGui.SetNextItemWidth(80);
			ImGui.SliderInt("Rs", ref PlaybackSettings.Rs, 0, 8);
			ImGui.SameLine();
			if (ImGui.Button("CREATE!"))
			{
				CreateChildPlayback();
			}
			ImGui.SameLine();
			ImGui.SetNextItemWidth(160);
			string PreviewLabel = SelectedIndex >= 0 && SelectedIndex < PlaybackManager.StoredPlaybacks.Count ? $"{PlaybackManager.StoredPlaybacks[SelectedIndex]}" : "Select Playback...";

			if (ImGui.BeginCombo("##PlaybackCombo", PreviewLabel))
			{
				for (int PlaybacksIndex = 0; PlaybacksIndex < PlaybackManager.StoredPlaybacks.Count; PlaybacksIndex++)
				{
					bool IsSelected = SelectedIndex == PlaybacksIndex;
					if (ImGui.Selectable($"{PlaybackManager.StoredPlaybacks[PlaybacksIndex]}", IsSelected))
					{
						SelectedIndex = PlaybacksIndex;
						var Playback = PlaybackManager.StoredPlaybacks[PlaybacksIndex];
						Console.WriteLine($"{Playback}");
						RequestAddition(Playback, new());
					}

					if (IsSelected)
						ImGui.SetItemDefaultFocus();
				}
				ImGui.EndCombo();
			}

			ImGui.SetNextItemWidth(80);
			ImGui.SliderFloat("Transpose", ref PlaybackSettings.TransposeSemitones, -12f, 12f);
			ImGui.SameLine();
			ImGui.SetNextItemWidth(80);
			ImGui.SliderFloat("Loudness", ref PlaybackSettings.Loudness, 0f, 4f);
			ImGui.SameLine();
			ImGui.SetNextItemWidth(40);
			ImGui.SliderFloat("L Volume", ref PlaybackSettings.LeftLoundness, 0f, 1f);
			ImGui.SameLine();
			ImGui.SetNextItemWidth(40);
			ImGui.SliderFloat("R Volume", ref PlaybackSettings.RightLoundness, 0f, 1f);
			ImGui.SameLine();
			ImGui.SetNextItemWidth(80);
			ImGui.SliderFloat("PanBaseVolume", ref PlaybackSettings.PanBaseVolume, 0f, 1f);
			ImGui.SameLine();
			ImGui.Text($"{GetTotalDuration().TotalSeconds}s");
			ImGui.SameLine();

			//if (IsInputValid)
			//{
			//	ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0f, 0f, 1f));
			//}
			ImGui.SetNextItemWidth(320);
			ImGui.InputText("Expression", ref PlaybackSettings.WaveExpression, 1024);
			//if (IsInputValid)
			//{
			//	ImGui.PopStyleColor();
			//}

			float Start = PlaybackSettings.TrimStart / PlaybackManager.BitRatePerMillisecond;
			float End = PlaybackSettings.TrimEnd / PlaybackManager.BitRatePerMillisecond;

			ImGui.SetNextItemWidth(160);
			ImGui.SliderFloat("START", ref Start, 0f, (float) GetTotalDuration().TotalMilliseconds);
			ImGui.SameLine();
			ImGui.SetNextItemWidth(160);
			ImGui.SliderFloat("E.N.D.", ref End, 0f, (float) GetTotalDuration().TotalMilliseconds);
			ImGui.SameLine();
			float MsPlayed = float.Lerp(0f, (float) GetTotalDuration().TotalMilliseconds, PlaybackProgress);
			ImGui.Text($"{MsPlayed}/{GetTotalDuration().TotalMilliseconds}");

			PlaybackSettings.TrimStart = (long) (Start * PlaybackManager.BitRatePerMillisecond);
			PlaybackSettings.TrimEnd = (long) (End * PlaybackManager.BitRatePerMillisecond);

			if (!IsRecording)
			{
				TimelineOrigin = ImGui.GetCursorScreenPos();

				var TotalWidth = PixelsPerSecond * (float) GetTotalDuration().TotalSeconds;
				DrawList.AddRectFilled(TimelineOrigin, TimelineOrigin + new Vector2(TotalWidth, MainHeight), MainColour);

				var ChildColour = ImGui.GetColorU32(new Vector4(0.25f, 0.72f, 0.45f, 1.0f));
				float ChildRowY = TimelineOrigin.Y + MainHeight + ChildGap;

				float ChildOffsetY = 0f;
				foreach (var (Child, EmplaceTime) in Children)
				{
					if (!Child.HasRecording)
					{
						continue;
					}

					float ChildStartX = TimelineOrigin.X + PixelsPerSecond * (float) EmplaceTime.TotalSeconds;
					float ChildWidth = PixelsPerSecond * (float) Child.GetTotalDuration().TotalSeconds;

					var ChildPos = new Vector2(ChildStartX, ChildRowY + ChildOffsetY);
					DrawList.AddRectFilled(ChildPos, ChildPos + new Vector2(ChildWidth, ChildHeight), ChildColour);
					ChildOffsetY += ChildHeight + ChildGap;
				}

				float TotalHeight = MainHeight + ChildOffsetY;

				if (IsPlaying)
				{
					float CursorX = TimelineOrigin.X + float.Lerp(0f, TotalWidth, PlaybackProgress);
					var CursorPos = new Vector2(CursorX, TimelineOrigin.Y);
					DrawList.AddRectFilled(CursorPos, CursorPos + new Vector2(3f, TotalHeight), ImGui.GetColorU32(new Vector4(1f, 0f, 0f, 1.0f)));
				}

				ImGui.Dummy(new Vector2(TotalWidth, TotalHeight));
			}

			foreach (var (_, _) in Children)
			{
				ImGui.NewLine();
			}

			if (ImGui.Button("save"))
			{
				if (!PlaybackManager.StoredPlaybacks.Contains(this))
				{
					PlaybackManager.StoredPlaybacks.Add(this);
				}
			}

			if (ImGui.Button("export"))
			{
				ExportProvider(out var Export);
				PlaybackManager.ExportProviders(Export);
			}

			RefreshSettings();
		}

		if (IsChild)
		{
			ImGui.NewLine();
			if (ImGui.Button("Beam me UP"))
			{
				MergeRequested.Invoke(this, TimeSpan.FromSeconds(5));
			}
			ImGui.SameLine();
			if (ImGui.Button("FUCK me"))
			{
				PlaybackManager.RemovePlayback(this);
			}
			ImGui.End();
		}

		ImGui.PopID();
	}

	private void CreateChildPlayback()
	{
		OAudioPlayback ChildPlayback = new()
		{
			IsChild = true,
		};

		ChildPlayback.MergeRequested += RequestAddition;
		PlaybackManager.AddPlayback(ChildPlayback);
	}
}
