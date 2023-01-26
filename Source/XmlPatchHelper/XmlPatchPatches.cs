﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Xml;
using Verse;
using RimWorld;
using UnityEngine;
using HarmonyLib;

namespace XmlPatchHelper
{
	[StaticConstructorOnStartup]
	public static class XmlPatchPatches
	{
		public const string LogLabel = "[XmlPatchHelper]";

		internal static Texture2D MenuIcon = ContentFinder<Texture2D>.Get("XmlPatchHelper_MenuIcon");

		internal static string CurrentVersion { get; private set; }

		internal static Harmony HarmonyInstance { get; private set; }

		static XmlPatchPatches()
		{
			HarmonyInstance = new Harmony("smashphil.xmlpatchhelper");

			Version version = Assembly.GetExecutingAssembly().GetName().Version;
			CurrentVersion = $"{version.Major}.{version.Minor}.{version.Build}";
			Log.Message($"<color=orange>{LogLabel}</color> version {CurrentVersion}");

			XmlPatchConsole.ExcludeField(typeof(PatchOperation), nameof(PatchOperation.sourceFile));

			HarmonyInstance.Patch(AccessTools.Method(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing)),
				prefix: new HarmonyMethod(typeof(XmlPatchPatches),
				nameof(InsertXmlPatcherButton)));
		}

		public static void InsertXmlPatcherButton(ref List<ListableOption> optList)
		{
			if (optList.Any(opt => opt is ListableOption_WebLink))
			{
				optList.Add(new ListableOption_WebLink("XmlPatchHelper".Translate(), delegate ()
				{
					Find.WindowStack.Add(new XmlPatchConsole());
				}, MenuIcon));
			}
		}
	}
}
