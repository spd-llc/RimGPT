﻿using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

// TODO: things to report:
// - injuries
// - raider ai
// - player changes to config
// - player designating (buttons & construction)

namespace RimGPT
{
	// run our logger
	//
	[HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsOnGUI))]
	public static class LongEventHandler_LongEventsOnGUI_Patch
	{
		public static void Postfix()
		{
			Logger.Log();
		}
	}

	// add welcome - need to configure message
	//
	[HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
	public static class MainMenuDrawer_MainMenuOnGUI_Patch
	{
		static bool showWelcome = true;
		static readonly Color background = new(0f, 0f, 0f, 0.8f);

		public static void Postfix()
		{
			if (showWelcome == false || RimGPTMod.Settings.IsConfigured)
			{
				UIRoot_Play_UIRootOnGUI_Patch.Postfix();
				return;
			}

			var (sw, sh) = (UI.screenWidth, UI.screenHeight);
			var (w, h) = (360, 120);
			var rect = new Rect((sw - w) / 2, (sh - h) / 2, w, h);
			var welcome = "Welcome to RimGPT. You need to configure the mod before you can use it. Click here.";

			Widgets.DrawBoxSolidWithOutline(rect, background, Color.white);
			if (Mouse.IsOver(rect) && Input.GetMouseButton(0))
			{
				showWelcome = false;
				Find.WindowStack.Add(new Dialog_ModSettings(RimGPTMod.self));
			}
			var anchor = Text.Anchor;
			var font = Text.Font;
			Text.Font = GameFont.Small;
			Text.Anchor = TextAnchor.MiddleCenter;
			Widgets.Label(rect.ExpandedBy(-20, 0), welcome);
			Text.Anchor = anchor;
			Text.Font = font;
		}
	}

	// add toggle button to play settings
	//
	[StaticConstructorOnStartup]
	[HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
	public static class PlaySettings_DoPlaySettingsGlobalControls_Patch
	{
		static readonly Texture2D icon = ContentFinder<Texture2D>.Get("ToggleAI");
		public static void Postfix(WidgetRow row, bool worldView)
		{
			if (worldView)
				return;
			var previousState = RimGPTMod.Settings.enabled;
			row.ToggleableIcon(ref RimGPTMod.Settings.enabled, icon, $"RimGPT is {(RimGPTMod.Settings.enabled ? "ON" : "OFF")}".Translate(), SoundDefOf.Mouseover_ButtonToggle);
			if (previousState != RimGPTMod.Settings.enabled)
				RimGPTMod.Settings.Write();
		}
	}

	// draw spoken content as text
	//
	[HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
	public static class UIRoot_Play_UIRootOnGUI_Patch
	{
		static readonly Color background = new(0f, 0f, 0f, 0.4f);

		public static void Postfix()
		{
			var welcome = Personas.currentText;
			if (welcome == "")
				return;

			var (sw, sh) = (UI.screenWidth, UI.screenHeight);
			var (w, h) = (800, 180);
			var rect = new Rect((sw - w) / 2, (sh - h) / 2 + sh / 3, w, h);

			Widgets.DrawBoxSolid(rect, background);
			var anchor = Text.Anchor;
			var font = Text.Font;
			Text.Font = GameFont.Medium;
			Text.Anchor = TextAnchor.MiddleCenter;
			Widgets.Label(rect.ExpandedBy(-20, 0), welcome);
			Text.Anchor = anchor;
			Text.Font = font;
		}
	}

	// reset history when going back to the main menu
	//
	[HarmonyPatch(typeof(GenScene), nameof(GenScene.GoToMainMenu))]
	public static class GenScene_GoToMainMenu_Patch
	{
		public static void Postfix()
		{
			Personas.Reset("The player went to the main menu");
		}
	}

	// reset history when a game is loaded
	//
	[HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.LoadGame), [typeof(string)])]
	public static class GameDataSaveLoader_LoadGame_Patch
	{
		public static void Postfix(string saveFileName)
		{
			Personas.Reset($"The player loaded the game file '{saveFileName}'");
		}
	}

	// send game started info
	//
	[HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
	public static class Game_FinalizeInit_Patch
	{
		public static void Postfix(Game __instance)
		{
			var colonists = __instance.Maps.SelectMany(m => m.mapPawns.FreeColonists).Join(c => c.LabelShortCap);
			Personas.Add($"{"GeneratingWorld".Translate()}. {"ColonistsSection".Translate()}: {colonists}", 5);
		}
	}

	// send mod changes
	//
	[HarmonyPatch(typeof(Page_ModsConfig), nameof(Page_ModsConfig.DoModList))]
	public static class Page_ModsConfig_DoModList_Patch
	{
		static HashSet<string> prevModList = null;

		public static void Postfix(Rect modListArea, List<ModMetaData> modList)
		{
			if (modListArea.x == 0)
				return;

			var newModList = new HashSet<string>(modList.Select(m => m.Name));
			prevModList ??= newModList;

			var removedMods = prevModList.Except(newModList).Join();
			if (removedMods != "")
				Personas.Add($"The player removed these mods: {removedMods}", 1);

			var addedMods = newModList.Except(prevModList).Join();
			if (addedMods != "")
				Personas.Add($"The player added these mods: {addedMods}", 1);

			prevModList = newModList;
		}
	}

	// send scenario selection
	//
	[HarmonyPatch(typeof(Page_SelectScenario), nameof(Page_SelectScenario.DoScenarioListEntry))]
	public static class Page_SelectScenario_DoScenarioListEntry_Patch
	{
		public static void Postfix(Page_SelectScenario __instance)
		{
			Differ.IfChangedPersonasAdd("scenario", __instance.curScen?.name, "The player chose scenario '{VALUE}'", 5);
		}
	}

	// send storyteller selection
	//
	[HarmonyPatch(typeof(StorytellerUI), nameof(StorytellerUI.DrawStorytellerSelectionInterface))]
	public static class StorytellerUI_DrawStorytellerSelectionInterface_Patch
	{
		public static void Postfix(StorytellerDef chosenStoryteller, DifficultyDef difficulty)
		{
			Differ.IfChangedPersonasAdd("storyteller", chosenStoryteller?.LabelCap.ToString(), "The player chose storyteller '{VALUE}'", 5);
			Differ.IfChangedPersonasAdd("difficulty", difficulty?.LabelCap.ToString(), "The player chose difficulty '{VALUE}'", 5);
		}
	}

	// send randomizing colonist
	//
	[HarmonyPatch(typeof(StartingPawnUtility), nameof(StartingPawnUtility.RandomizeInPlace))]
	public static class StartingPawnUtility_RandomizeInPlace_Patch
	{
		public static void Postfix(Pawn p, Pawn __result)
		{
			Personas.Add($"Player clicks 'Randomize' and replaces {p.LabelShortCap} with {__result.LabelShortCap}", 1);
		}
	}

	// send colonist details while choosing starting pawns
	//
	[HarmonyPatch(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.DrawPortraitArea))]
	public static class Page_ConfigureStartingPawns_DrawPortraitArea_Patch
	{
		static Pawn pawn = null;
		static readonly Debouncer debouncer = new(1000);

		static string StartingColonists()
		{
			return Find.GameInitData.startingAndOptionalPawns.Take(Find.GameInitData.startingPawnCount).Join(p => p.LabelShortCap);
		}

		public static void Postfix(Pawn ___curPawn)
		{
			if (___curPawn == pawn)
				return;
			pawn = ___curPawn;

			debouncer.Debounce(() =>
			{
				var backstory = pawn.story.GetBackstory(BackstorySlot.Adulthood) ?? pawn.story.GetBackstory(BackstorySlot.Childhood);
				var allTraits = pawn.story.traits.allTraits;
				var traits = allTraits.Count > 0 ? ", " + allTraits.Select(t => t.LabelCap).ToCommaList() : "";

				var disabled = CharacterCardUtility.WorkTagsFrom(pawn.CombinedDisabledWorkTags).Select(t => t.ToString()).ToCommaList();
				if (disabled.Any())
					disabled = $"{"IncapableOf".Translate(pawn)} {disabled}";
				if (disabled.Any())
					disabled = $", {disabled}";

				static string SkillName(SkillDef def, int level) => level == 0 ? $"No {def.LabelCap}" : $"{def.LabelCap}:{pawn.skills.GetSkill(def).Level}";
				var skills = pawn.skills.skills.Select(s => SkillName(s.def, pawn.skills.GetSkill(s.def).Level)).ToCommaList();
				if (skills.Any())
					skills = $", {skills}";

				var stats = $"{pawn.gender}, {pawn.ageTracker.AgeBiologicalYears}, {backstory.TitleCapFor(pawn.gender)}{traits}{disabled}{skills}";
				Personas.Add($"Player considers {___curPawn.LabelShortCap} ({stats}) for this new game", 1);
			});

			Differ.IfChangedPersonasAdd("starting-pawns", StartingColonists(), "Current starting colonists: {VALUE}", 2);
		}
	}

	// send game seed changes
	//
	[HarmonyPatch(typeof(Page_CreateWorldParams), nameof(Page_CreateWorldParams.DoWindowContents))]
	public static class Page_CreateWorldParams_DoWindowContents_Patch
	{
		static readonly Debouncer debouncer = new(2000);

		static string TextField(Rect rect, string text)
		{
			var result = Widgets.TextField(rect, text);
			if (result != text)
				debouncer.Debounce(() => Personas.Add($"Player changed the game seed to '{result}'", 2));
			return result;
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = SymbolExtensions.GetMethodInfo(() => Widgets.TextField(default, default));
			var to = SymbolExtensions.GetMethodInfo(() => TextField(default, default));
			return instructions.MethodReplacer(from, to);
		}
	}

	// send starting tile selection
	//
	[HarmonyPatch(typeof(WITab_Terrain), nameof(WITab_Terrain.FillTab))]
	public static class WorldInterface_SelectedTile_Setter_Patch
	{
		public static void Postfix(WITab_Terrain __instance)
		{
			var selTile = __instance.SelTile;
			var selTileID = __instance.SelTileID;
			var type = selTile.biome.LabelCap.ToString();
			var hills = selTile.hilliness.GetLabelCap();
			var stones = (from rt in Find.World.NaturalRockTypesIn(selTileID) select rt.label).ToCommaList(true, false).CapitalizeFirst();
			var grow = Zone_Growing.GrowingQuadrumsDescription(selTileID);
			var description = $"{type}, {hills}, {stones}, growing: {grow}";
			Differ.IfChangedPersonasAdd("starting-tile", description, "Player changed the starting tile to '{VALUE}'", 2);
		}
	}

	// send started jobs
	//
	[HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
	[HarmonyPatch(new Type[] { typeof(Job), typeof(JobCondition), typeof(ThinkNode), typeof(bool), typeof(bool), typeof(ThinkTreeDef), typeof(JobTag?), typeof(bool), typeof(bool), typeof(bool?), typeof(bool), typeof(bool) })]
	public static class Pawn_JobTracker_StartJob_Patch
	{
		static void Handle(Pawn_JobTracker tracker, JobDriver curDriver)
		{
			tracker.curDriver = curDriver;

			var pawn = tracker.pawn;
			if (pawn == null || pawn.AnimalOrWildMan())
				return;

			var job = curDriver.job;
			if (job == null)
				return;

			var workType = job.workGiverDef?.workType;
			if (workType == WorkTypeDefOf.Hauling)
				return;
			if (workType == WorkTypeDefOf.Construction)
				return;
			if (workType == WorkTypeDefOf.PlantCutting)
				return;
			if (workType == WorkTypeDefOf.Mining)
				return;
			if (workType == Defs.Cleaning)
				return;

			var defName = job.def.defName;
			if (defName == null)
				return;
			if (defName.StartsWith("Wait"))
				return;
			if (defName.StartsWith("Goto"))
				return;

			var report = curDriver.GetReport();
			report = report.Replace(pawn.LabelShortCap, pawn.NameAndType());
			if (job.targetA.Thing is Pawn target)
				report = report.Replace(target.LabelShortCap, target.NameAndType());

			Personas.Add($"{pawn.NameAndType()} {report}", 3);
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			return new CodeMatcher(instructions)
				 .MatchStartForward(new CodeMatch(CodeInstruction.StoreField(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.curDriver))))
				 .SetInstruction(CodeInstruction.Call(() => Handle(null, null)))
				 .Instructions();
		}
	}

	// send pawn log
	//
	[HarmonyPatch]
	public static class Battle_Add_Patch
	{
		public static IEnumerable<MethodBase> TargetMethods()
		{
			yield return SymbolExtensions.GetMethodInfo(() => new Battle().Add(null));
			yield return SymbolExtensions.GetMethodInfo(() => new PlayLog().Add(null));
		}

		public static void Postfix(LogEntry entry)
		{
			string text;
			Tools.ExtractPawnsFromLog(entry, out var from, out var to);
			text = entry.ToGameStringFromPOVWithType(from);
			if (text != null)
				Personas.Add(text, 1);
			text = entry.ToGameStringFromPOVWithType(to);
			if (text != null)
				Personas.Add(text, 1);
		}
	}

	// send pawn text motes
	//
	[HarmonyPatch(typeof(MoteMaker), nameof(MoteMaker.ThrowText))]
	[HarmonyPatch(new Type[] { typeof(Vector3), typeof(Map), typeof(string), typeof(float) })]
	public static class MoteMaker_ThrowText_Patch
	{
		public static void Postfix(Vector3 loc, Map map, string text)
		{
			var pawns = map.mapPawns.FreeColonistsSpawned.Where(pawn => (pawn.DrawPos - loc).MagnitudeHorizontalSquared() < 4f);
			if (pawns.Count() != 1)
				return;
			var pawn = pawns.First();
			text = text.Replace("\n", " ");
			Personas.Add($"{pawn.NameAndType()}: \"{text}\"", 0);
		}
	}

	// send game letters
	//
	[HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter))]
	[HarmonyPatch(new Type[] { typeof(Letter), typeof(string) })]
	public static class LetterStack_ReceiveLetter_Patch
	{
		public static void Postfix(Letter let)
		{
			if (let.CanShowInLetterStack == false)
				return;
			var label = let.Label;
			var text = let.GetMouseoverText().Replace("\n", " ");
			Personas.Add($"{Tools.Strings.information}: {label} - {text}", 3);
		}
	}

	// send game alerts
	//
	[HarmonyPatch(typeof(AlertsReadout), nameof(AlertsReadout.CheckAddOrRemoveAlert))]
	public static class AlertsReadout_CheckAddOrRemoveAlert_Patch
	{
		public static void Prefix(Alert alert, List<Alert> ___activeAlerts, out (bool, string) __state)
		{
			__state = (___activeAlerts.Contains(alert), alert.Label);
		}

		public static void Postfix(Alert alert, List<Alert> ___activeAlerts, (bool, string) __state)
		{
			var wasInList = __state.Item1;
			var isInList = ___activeAlerts.Contains(alert);
			if (wasInList == false && isInList)
				Personas.Add($"{Tools.Strings.information}: {alert.Label}", 4);
			if (wasInList && isInList == false)
			{
				var alertLabel = __state.Item2;
				Personas.Add($"{Tools.Strings.completed}: {alertLabel}", 5);
			}
		}
	}

	// send game messages
	//
	[HarmonyPatch(typeof(Messages), nameof(Messages.Message))]
	[HarmonyPatch(new Type[] { typeof(Message), typeof(bool) })]
	public static class Messages_Message_Patch
	{
		public static void Postfix(Message msg)
		{
			Personas.Add(msg.text, 2);
		}
	}

	// send finished construction
	//
	[HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
	public static class TaleRecorder_RecordTale_Patch
	{
		public static void Postfix(Frame __instance, Pawn worker)
		{
			var def = __instance.BuildDef;
			if (def == null || def == Defs.Wall || worker == null)
				return;
			var makeStr = "RecipeMakeJobString".Translate(def.LabelCap);
			Personas.Add($"{worker.NameAndType()}: {Tools.Strings.finished} {makeStr}", 1);
		}
	}

	// send work priority changes
	//
	[HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.DrawWorkBoxFor))]
	public static class WidgetsWork_DrawWorkBoxFor_Patch
	{
		public static void SetPriority(Pawn_WorkSettings instance, WorkTypeDef w, int priority)
		{
			instance.SetPriority(w, priority);
			var workType = w.labelShort.CapitalizeFirst();
			Personas.Add($"{instance.pawn.NameAndType()}: {Tools.Strings.priority} {workType} = {priority}", 2);
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = SymbolExtensions.GetMethodInfo(() => new Pawn_WorkSettings().SetPriority(null, 0));
			var to = SymbolExtensions.GetMethodInfo(() => SetPriority(null, null, 0));
			return instructions.MethodReplacer(from, to);
		}
	}

	// send resource count from time to time
	//
	[HarmonyPatch(typeof(UIRoot), nameof(UIRoot.UIRootUpdate))]
	public static class UIRoot_UIRootUpdate_Patch
	{
		static int lastTicks = 0;
		static int lastTotal = -1;
		static readonly HashSet<ThingCategoryDef> thingCategories =
		[
			ThingCategoryDefOf.Foods,
			ThingCategoryDefOf.FoodMeals,
			ThingCategoryDefOf.Medicine,
			ThingCategoryDefOf.StoneBlocks,
			ThingCategoryDefOf.Manufactured,
			ThingCategoryDefOf.ResourcesRaw
		];

		public static bool Reportable(this KeyValuePair<ThingDef, int> pair)
		{
			if (pair.Value == 0)
				return false;
			var hashSet = pair.Key.thingCategories.ToHashSet();
			return hashSet.Intersect(thingCategories).Any();
		}

		public static void Postfix()
		{
			var map = Find.CurrentMap;
			if (map == null)
				return;

			lastTicks++;
			if (lastTicks >= 12000)
			{
				lastTicks = 0;
				var amounts = Find.CurrentMap.resourceCounter.AllCountedAmounts.Where(Reportable).ToArray();
				var total = amounts.Sum(pair => pair.Value);
				if (amounts.Any() && total != lastTotal)
				{
					lastTotal = total;
					var colonistCount = Find.CurrentMap.mapPawns.FreeColonistsCount;
					var amountList = amounts.Join(pair => $"{pair.Value} {pair.Key.LabelCap}");
					Personas.Add($"Minor update: total {colonistCount} colonists, {amountList}", 2);
				}
			}
		}
	}

	// add a keyboard shortcut to the mod settings dialog
	//
	[HarmonyPatch(typeof(GlobalControls), nameof(GlobalControls.GlobalControlsOnGUI))]
	public static class GlobalControls_GlobalControlsOnGUI_Patch
	{
		public static void Postfix()
		{
			if (Event.current.type == EventType.KeyDown && Defs.Command_OpenRimGPT.KeyDownEvent)
			{
				var stack = Find.WindowStack;
				if (stack.IsOpen<Dialog_ModSettings>() == false)
				{
					var me = LoadedModManager.GetMod<RimGPTMod>();
					var dialog = new Dialog_ModSettings(me);
					stack.Add(dialog);
				}
				Event.current.Use();
			}
		}
	}
}