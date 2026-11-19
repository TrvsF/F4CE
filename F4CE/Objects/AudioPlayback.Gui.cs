using ImGuiNET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace F4CE.Objects;

public record FPlaybackSettings
{
	public string WaveExpression = "f";
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

	public readonly string Mp3ImportFolder = "C:/Users/TravisF/Desktop/LOVEANDMISERY/wav";
	public List<string> SMp3FilePaths = new();
	public int SMp3SelectedIndex = -1;
	public bool SMp3FolderScanned = false;

	private int LoopTimes = 1;
	private int SelectedIndex = -1;
	private float ChildAddSeconds = 0;
	private bool PlayParentWhenRecording = false;
	private enum EKidDrag { Move, TrimStart, TrimEnd }
	private EKidDrag KidDragMode = EKidDrag.Move;
	private bool KidWasDragged = false;
	private double KidDragAt0, KidDragFrom0, KidDragTo0, KidDragLength0; // the kid as it was when the press started
	private double KidDragAt, KidDragFrom, KidDragTo; // where the drag has got to

	private EKidDrag MainDragMode = EKidDrag.Move;
	private bool MainWasDragged = false;
	private double MainDragFrom0, MainDragTo0, MainDragLength0; // the main bar as it was when the press started
	private double MainDragFrom, MainDragTo; // where the drag has got to

	private const float KidEdgeGrab = 6f; // px at each end of a kid's bar that trim it instead of moving it
	private const double KidMinSeconds = 0.1; // shortest a kid can be trimmed down to

	private const float PixelsPerSecond = 20f;
	private const float MainHeight = 40f;
	private const float ChildHeight = 28f;
	private const float ChildGap = 4f;

	private string ChildWindowName => $"kidnamed{ImGuiD}";

	public void DrawBlock()
	{
		ImGui.PushID(ImGuiD.ToString());

		if (IsChild)
		{
			ImGui.Begin(ChildWindowName);
		}

		// TODO : wrap me!
		// (fetched after Begin so a child window draws its timeline into its own window)
		var DrawList = ImGui.GetWindowDrawList();
		var TimelineOrigin = ImGui.GetCursorScreenPos();
		var MainColour = ImGui.GetColorU32(new Vector4(0.20f, 0.55f, 0.95f, 1.0f));

		if (!HasRecording)
		{
			if (!IsRecording)
			{
				if (ImGui.Button("Start Recording", new Vector2(160, 20)))
				{
					StartRecording();
					if (IsChild)
					{
						PlayRecording?.Invoke();
					}
				}
				
				ImGui.SliderFloat("Silence Length (Seconds)", ref PlaybackSettings.SilenceSeconds, 0f, 120f);
				if (ImGui.Button("Create Silent Playback", new Vector2(160, 20)))
				{
					SetSilence(TimeSpan.FromSeconds(PlaybackSettings.SilenceSeconds));
					PlaybackSettings.WaveExpression = "sin(t*PI*100)";
					PlaybackSettings.TrimEnd = (long) (PlaybackSettings.SilenceSeconds * PlaybackManager.BitRate);
				}

				bool Refresh = ImGui.Button("Refresh##Mp3Refresh");

				if (!SMp3FolderScanned || Refresh)
				{
					SMp3FilePaths = Directory.Exists(Mp3ImportFolder) ? new List<string>(Directory.GetFiles(Mp3ImportFolder, "*.wav", SearchOption.TopDirectoryOnly)) : [];

					SMp3SelectedIndex = -1;
					SMp3FolderScanned = true;
				}

				ImGui.SameLine();

				string PreviewLabel = SMp3SelectedIndex >= 0 && SMp3SelectedIndex < SMp3FilePaths.Count ? Path.GetFileNameWithoutExtension(SMp3FilePaths[SMp3SelectedIndex]) : SMp3FilePaths.Count > 0 ? "Select MP3..." : "(no files found)";

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
				ImGui.NewLine();
			}
			else
			{
				if (ImGui.Button("Stop Recording", new Vector2(160, 20)))
				{
					StopRecording();
					if (IsChild)
					{
						StopPlayingRecording?.Invoke();
					}
					// PlaybackSettings.TrimEnd = (long)(GetTotalDuration().TotalSeconds * PlaybackManager.BitRate);
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
				PlaybackSettings = new();
				Children.Clear();
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
						AddChild(Playback, new());
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

			float StartTime = (float) (PlaybackSettings.TrimStart / (double) PlaybackManager.BitRate);
			float EndTime = (float) (PlaybackSettings.TrimEnd / (double) PlaybackManager.BitRate);
			float TotalSeconds = (float) GetTotalDuration().TotalSeconds;

			ImGui.SetNextItemWidth(160);
			if (ImGui.SliderFloat("START", ref StartTime, 0f, TotalSeconds))
			{
				PlaybackSettings.TrimStart = (long) Math.Round(StartTime * PlaybackManager.BitRate);
			}

			ImGui.SameLine();
			ImGui.SetNextItemWidth(160);
			if (ImGui.SliderFloat("E.N.D.", ref EndTime, 0f, TotalSeconds))
			{
				PlaybackSettings.TrimEnd = (long) Math.Round(EndTime * PlaybackManager.BitRate);
			}

			ImGui.SameLine();
			if (ImGui.Button("T-RIM"))
			{
				// no E.N.D. (-1) means keep everything to the end, kids included, so trimming just the start works too
				long TrimEndPos = PlaybackSettings.TrimEnd >= 0 ? PlaybackSettings.TrimEnd : (long) Math.Round(GetTotalDuration().TotalSeconds * PlaybackManager.BitRate);
				Trim(PlaybackSettings.TrimStart, TrimEndPos);
				PlaybackSettings.TrimStart = 0;
				PlaybackSettings.TrimEnd = -1; // what's left is exactly the trimmed part
			}

			ImGui.SameLine();
			ImGui.SetNextItemWidth(40);
			ImGui.SliderInt("Loop Times", ref LoopTimes, 1, 24);
			ImGui.SameLine();
			if (ImGui.Button("Loop"))
			{
				Loop(LoopTimes);
			}

			ImGui.SameLine();
			float MsPlayed = float.Lerp(0f, TotalSeconds, PlaybackProgress);
			ImGui.Text($"{MsPlayed:F2}/{TotalSeconds:F2}");

			if (!IsRecording)
			{
				TimelineOrigin = ImGui.GetCursorScreenPos();

				var TotalWidth = PixelsPerSecond * (float) GetTotalDuration().TotalSeconds;

				if (IsChild)
				{
					DrawList.AddRectFilled(TimelineOrigin, TimelineOrigin + new Vector2(TotalWidth, MainHeight), MainColour);
				}
				else
				{
					DrawMainBar(DrawList, TimelineOrigin, MainColour); // its own audio, with ends you can drag to trim
				}

				var ChildColour = ImGui.GetColorU32(new Vector4(0.25f, 0.72f, 0.45f, 1.0f));
				var ChildHoverColour = ImGui.GetColorU32(new Vector4(0.40f, 0.85f, 0.60f, 1.0f));
				var ChildGhostColour = ImGui.GetColorU32(new Vector4(0.25f, 0.72f, 0.45f, 0.25f));
				var ChildEdgeColour = ImGui.GetColorU32(new Vector4(0.90f, 1.00f, 0.90f, 1.0f));
				float ChildRowY = TimelineOrigin.Y + MainHeight + ChildGap;

				float ChildOffsetY = 0f;
				int CopyKidIndex = -1;
				for (int ChildIndex = 0; ChildIndex < Children.Count; ++ChildIndex)
				{
					var (Child, EmplaceTime) = Children[ChildIndex];

					if (!Child.HasRecording)
					{
						continue;
					}

					// the kid plays KidFrom..KidTo seconds of its recording (KidLength long untrimmed), starting at KidAt on this timeline
					var (KidFrom, KidTo, KidLength) = Child.GetTrimWindow();
					double KidAt = EmplaceTime.TotalSeconds;
					float RowY = ChildRowY + ChildOffsetY;

					// one button over the part that plays: its ends trim it, the middle moves it, a plain click opens it
					var BarPos = new Vector2(TimelineOrigin.X + PixelsPerSecond * (float) KidAt, RowY);
					var BarSize = new Vector2(MathF.Max(PixelsPerSecond * (float) (KidTo - KidFrom), 1f), ChildHeight);
					ImGui.SetCursorScreenPos(BarPos);
					bool Clicked = ImGui.InvisibleButton($"##kid{Child.ImGuiD}", BarSize);
					bool IsHovered = ImGui.IsItemHovered();
					bool IsActive = ImGui.IsItemActive();
					bool JustPressed = ImGui.IsItemActivated();
					bool JustReleased = ImGui.IsItemDeactivated();

					if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
					{
						CopyKidIndex = ChildIndex; // copied after the loop, so the list doesn't change under it
					}

					float Grab = MathF.Min(KidEdgeGrab, BarSize.X / 3f);
					float MouseX = ImGui.GetMousePos().X;
					EKidDrag HoverMode = MouseX < BarPos.X + Grab ? EKidDrag.TrimStart
						: MouseX > BarPos.X + BarSize.X - Grab ? EKidDrag.TrimEnd
						: EKidDrag.Move;

					if (JustPressed)
					{
						KidDragMode = HoverMode;
						KidWasDragged = false;
						KidDragAt0 = KidDragAt = KidAt;
						KidDragFrom0 = KidDragFrom = KidFrom;
						KidDragTo0 = KidDragTo = KidTo;
						KidDragLength0 = KidLength;
					}

					if (IsActive && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
					{
						// measured from where the press started, so the bar (or edge) stays under the mouse
						double Delta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).X / PixelsPerSecond;
						KidWasDragged = true;

						if (KidDragMode == EKidDrag.Move)
						{
							KidDragAt = Math.Max(0.0, KidDragAt0 + Delta);
							ImGui.SetTooltip($"{KidDragAt:F2}s");
						}
						else if (KidDragMode == EKidDrag.TrimStart)
						{
							// the audio stays where it is on the timeline, so trimming the start makes the kid start later
							// (and un-trimming earlier), but never before the start of its recording or before 0s
							double Earliest = Math.Max(0.0, KidDragFrom0 - KidDragAt0);
							double Latest = Math.Max(KidDragFrom0, KidDragTo0 - KidMinSeconds);
							KidDragFrom = Math.Min(Math.Max(KidDragFrom0 + Delta, Earliest), Latest);
							KidDragAt = KidDragAt0 + (KidDragFrom - KidDragFrom0);
							ImGui.SetTooltip($"start {KidDragFrom:F2}s");
						}
						else
						{
							double Shortest = Math.Min(KidDragFrom0 + KidMinSeconds, KidDragTo0);
							KidDragTo = Math.Min(Math.Max(KidDragTo0 + Delta, Shortest), KidDragLength0);
							ImGui.SetTooltip($"end {KidDragTo:F2}s");
						}
					}
					else if (IsHovered)
					{
						ImGui.SetTooltip(HoverMode == EKidDrag.Move ? "click to open, drag to move, right-click to copy" : "drag to trim");
					}

					if (JustReleased && KidWasDragged)
					{
						// saved once, on release (the playback thread reads the kid list too)
						if (KidDragMode != EKidDrag.Move)
						{
							Child.SetTrimWindow(KidDragFrom, KidDragTo);
						}

						EmplaceTime = TimeSpan.FromSeconds(KidDragAt);
						Children[ChildIndex] = (Child, EmplaceTime);
						Child.ChildAddSeconds = (float) KidDragAt; // keep its Insert Time slider in step
					}
					else if (Clicked && !KidWasDragged)
					{
						// a drag can also end in a "click", so only a plain click opens the window
						OpenChild(Child, EmplaceTime);
					}

					// draw it where it's being dragged to (or where the drag just left it)
					if ((IsActive || JustReleased) && KidWasDragged)
					{
						(KidAt, KidFrom, KidTo) = (KidDragAt, KidDragFrom, KidDragTo);
					}

					float PlayX = TimelineOrigin.X + PixelsPerSecond * (float) KidAt;
					float PlayW = MathF.Max(PixelsPerSecond * (float) (KidTo - KidFrom), 1f);

					// the trimmed-off audio, faded, so you can see what can be dragged back out
					float RecordingX = MathF.Max(TimelineOrigin.X, PlayX - PixelsPerSecond * (float) KidFrom);
					float RecordingEndX = PlayX + PixelsPerSecond * (float) (KidLength - KidFrom);
					DrawList.AddRectFilled(new Vector2(RecordingX, RowY), new Vector2(RecordingEndX, RowY + ChildHeight), ChildGhostColour);

					DrawList.AddRectFilled(new Vector2(PlayX, RowY), new Vector2(PlayX + PlayW, RowY + ChildHeight), IsHovered || IsActive ? ChildHoverColour : ChildColour);

					// light up the end that's being (or would be) trimmed
					EKidDrag EdgeMode = IsActive ? KidDragMode : HoverMode;
					if ((IsActive || IsHovered) && EdgeMode != EKidDrag.Move)
					{
						ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
						float EdgeX = EdgeMode == EKidDrag.TrimStart ? PlayX : PlayX + PlayW - 2f;
						DrawList.AddRectFilled(new Vector2(EdgeX, RowY), new Vector2(EdgeX + 2f, RowY + ChildHeight), ChildEdgeColour);
					}

					ChildOffsetY += ChildHeight + ChildGap;
				}

				// the copy is fully separate from the original, and goes straight after it (on the row just below)
				if (CopyKidIndex >= 0)
				{
					var (Original, OriginalAt) = Children[CopyKidIndex];
					var (OriginalFrom, OriginalTo, _) = Original.GetTrimWindow();
					Children.Insert(CopyKidIndex + 1, (Original.Duplicate(), OriginalAt + TimeSpan.FromSeconds(OriginalTo - OriginalFrom)));
				}

				float TotalHeight = MainHeight + ChildOffsetY;

				if (IsPlaying)
				{
					float CursorX = TimelineOrigin.X + float.Lerp(0f, TotalWidth, PlaybackProgress);
					var CursorPos = new Vector2(CursorX, TimelineOrigin.Y);
					DrawList.AddRectFilled(CursorPos, CursorPos + new Vector2(3f, TotalHeight), ImGui.GetColorU32(new Vector4(1f, 0f, 0f, 1.0f)));
				}

				// the kid bars moved the cursor, so reserve the whole timeline from its top-left
				ImGui.SetCursorScreenPos(TimelineOrigin);
				ImGui.Dummy(new Vector2(TotalWidth, TotalHeight));
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
			ImGui.SetNextItemWidth(80);
			ImGui.SliderFloat("Insert Time", ref ChildAddSeconds, 0f, 100f);
			ImGui.NewLine();
			if (ImGui.Button("Beam me UP"))
			{
				MergeRequested?.Invoke(this, TimeSpan.FromSeconds(ChildAddSeconds));
			}
			ImGui.SameLine();
			ImGui.Checkbox("Play Parent When Recording", ref PlayParentWhenRecording);
			ImGui.SameLine();
			if (ImGui.Button("FUCK me"))
			{
				RemoveRequested?.Invoke(this); // a kid that was already beamed up gets dropped from its parent too
				PlaybackManager.RemovePlayback(this);
			}
			ImGui.End();
		}

		ImGui.PopID();
	}

	// A top-level playback's own audio on its timeline: the part that plays is solid, the trimmed-off part
	// faded, and dragging either end sets its START/E.N.D. (non-destructive, T-RIM makes it permanent).
	private void DrawMainBar(ImDrawListPtr DrawList, Vector2 Origin, uint MainColour)
	{
		var (From, To, Length) = GetTrimWindow();

		var BarPos = new Vector2(Origin.X + PixelsPerSecond * (float) From, Origin.Y);
		var BarSize = new Vector2(MathF.Max(PixelsPerSecond * (float) (To - From), 1f), MainHeight);
		ImGui.SetCursorScreenPos(BarPos);
		ImGui.InvisibleButton("##mainbar", BarSize);
		bool IsHovered = ImGui.IsItemHovered();
		bool IsActive = ImGui.IsItemActive();
		bool JustPressed = ImGui.IsItemActivated();
		bool JustReleased = ImGui.IsItemDeactivated();

		float Grab = MathF.Min(KidEdgeGrab, BarSize.X / 3f);
		float MouseX = ImGui.GetMousePos().X;
		EKidDrag HoverMode = MouseX < BarPos.X + Grab ? EKidDrag.TrimStart
			: MouseX > BarPos.X + BarSize.X - Grab ? EKidDrag.TrimEnd
			: EKidDrag.Move; // the middle does nothing, the main bar doesn't move

		if (JustPressed)
		{
			MainDragMode = HoverMode;
			MainWasDragged = false;
			MainDragFrom0 = MainDragFrom = From;
			MainDragTo0 = MainDragTo = To;
			MainDragLength0 = Length;
		}

		if (IsActive && MainDragMode != EKidDrag.Move && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
		{
			// measured from where the press started, so the end stays under the mouse
			double Delta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).X / PixelsPerSecond;
			MainWasDragged = true;

			if (MainDragMode == EKidDrag.TrimStart)
			{
				MainDragFrom = Math.Min(Math.Max(MainDragFrom0 + Delta, 0.0), Math.Max(MainDragFrom0, MainDragTo0 - KidMinSeconds));
				ImGui.SetTooltip($"start {MainDragFrom:F2}s");
			}
			else
			{
				MainDragTo = Math.Min(Math.Max(MainDragTo0 + Delta, Math.Min(MainDragFrom0 + KidMinSeconds, MainDragTo0)), MainDragLength0);
				ImGui.SetTooltip($"end {MainDragTo:F2}s");
			}
		}
		else if (IsHovered && HoverMode != EKidDrag.Move)
		{
			ImGui.SetTooltip("drag to trim");
		}

		if (JustReleased && MainWasDragged)
		{
			SetTrimWindow(MainDragFrom, MainDragTo); // saved once, on release
		}

		// draw it where it's being dragged to (or where the drag just left it)
		if ((IsActive || JustReleased) && MainWasDragged)
		{
			(From, To) = (MainDragFrom, MainDragTo);
		}

		float PlayX = Origin.X + PixelsPerSecond * (float) From;
		float PlayW = MathF.Max(PixelsPerSecond * (float) (To - From), 1f);
		float RowBottom = Origin.Y + MainHeight;
		EKidDrag EdgeMode = IsActive ? MainDragMode : HoverMode;
		bool IsLit = (IsActive || IsHovered) && EdgeMode != EKidDrag.Move;

		var GhostColour = ImGui.GetColorU32(new Vector4(0.20f, 0.55f, 0.95f, 0.25f));
		var LitColour = ImGui.GetColorU32(new Vector4(0.35f, 0.68f, 1.00f, 1.0f));
		var EdgeColour = ImGui.GetColorU32(new Vector4(0.90f, 0.95f, 1.00f, 1.0f));

		// the whole recording faded, then the part that plays on top
		DrawList.AddRectFilled(Origin, new Vector2(Origin.X + PixelsPerSecond * (float) Length, RowBottom), GhostColour);
		DrawList.AddRectFilled(new Vector2(PlayX, Origin.Y), new Vector2(PlayX + PlayW, RowBottom), IsLit ? LitColour : MainColour);

		if (IsLit)
		{
			ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
			float EdgeX = EdgeMode == EKidDrag.TrimStart ? PlayX : PlayX + PlayW - 2f;
			DrawList.AddRectFilled(new Vector2(EdgeX, Origin.Y), new Vector2(EdgeX + 2f, RowBottom), EdgeColour);
		}
	}

	private void OpenChild(OAudioPlayback Child, TimeSpan EmplaceTime)
	{
		if (PlaybackManager.ActivePlaybacks.Contains(Child))
		{
			ImGui.SetWindowFocus(Child.ChildWindowName); // already open, bring it to the front
			return;
		}

		Child.IsChild = true; // kids picked from the saved list start out as top-level playbacks
		WireChild(Child); // kids loaded from disk have nothing hooked up to their parent yet
		Child.ChildAddSeconds = (float) EmplaceTime.TotalSeconds; // so "Beam me UP" keeps it where it is
		PlaybackManager.AddPlayback(Child);
	}
}
