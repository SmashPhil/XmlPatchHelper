using System;
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

		internal static Texture2D XPathHelperIcon = ContentFinder<Texture2D>.Get("XmlPatchHelperMenuIcon");

		internal static string CurrentVersion { get; private set; }

		internal static Harmony HarmonyInstance { get; private set; }

		static XmlPatchPatches()
		{
			HarmonyInstance = new Harmony("smashphil.xmlpatchhelper");

			Version version = Assembly.GetExecutingAssembly().GetName().Version;
			CurrentVersion = $"{version.Major}.{version.Minor}.{version.Build}";
			Log.Message($"{LogLabel} version {CurrentVersion}");

			XmlPatchConsole.ExcludeField(typeof(PatchOperation), nameof(PatchOperation.sourceFile));

			HarmonyInstance.Patch(AccessTools.Method(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing)),
				prefix: new HarmonyMethod(typeof(XmlPatchPatches),
				nameof(InsertXmlPatcherButton)));


			HarmonyInstance.Patch(AccessTools.Method(typeof(Printer_Plane), nameof(Printer_Plane.PrintPlane)),
				prefix: new HarmonyMethod(typeof(XmlPatchPatches),
				nameof(Test)));
		}

		private static void Test(SectionLayer layer, Vector3 center, Vector2 size, Material mat, float rot = 0f, bool flipUv = false, Vector2[] uvs = null, Color32[] colors = null, float topVerticesAltitudeBias = 0.01f, float uvzPayload = 0f)
		{
			if (mat.mainTexture.name == "MF_PalmTree1")
			{
				Log.Message($"Colors: {colors[0]}");
			}
		}

		public static void InsertXmlPatcherButton(Rect rect, ref List<ListableOption> optList)
		{
			if (optList.FirstOrDefault(opt => opt.label == "BuySoundtrack".Translate()) != null)
			{
				optList.Add(new ListableOption_WebLinkResized("XmlPatchHelper".Translate(), delegate ()
				{
					Find.WindowStack.Add(new XmlPatchConsole());
				}, XPathHelperIcon));
			}
		}
	}
}
