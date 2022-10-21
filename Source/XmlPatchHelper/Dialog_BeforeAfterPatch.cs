using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Text;
using Verse;
using RimWorld;
using UnityEngine;
using HarmonyLib;

namespace XmlPatchHelper
{
	public class Dialog_BeforeAfterPatch : Window
	{
		private string before;
		private string after;

		private bool success;

		private static Vector2 beforeScrollPosition;
		private static Vector2 afterScrollPosition;

		private List<XmlNode> parents;

		public Dialog_BeforeAfterPatch()
		{
			doCloseX = true;

			XmlDocument editDoc = (XmlDocument)XmlPatchConsole.CombinedXmlDoc.Clone();
			XmlNodeList matchList = editDoc.SelectNodes(XmlPatchConsole.xpath);

			parents = new List<XmlNode>();
			foreach (XmlNode node in matchList)
			{
				parents.Add(node.ParentNode.Name != "Defs" ? node.ParentNode : node);
			}

			StringBuilder beforeStringBuilder = new StringBuilder();
			XmlPatchConsole.BuildXmlSummary(parents, beforeStringBuilder);

			before = beforeStringBuilder.ToString();
			
			success = XmlPatchConsole.patchOperation.Apply(editDoc) && ValidateXml();

			StringBuilder afterStringBuilder = new StringBuilder();
			XmlPatchConsole.BuildXmlSummary(parents, afterStringBuilder);

			after = afterStringBuilder.ToString();
		}

		public override Vector2 InitialSize => new Vector2(Mathf.Max(UI.screenWidth / 1.25f, 1200), Mathf.Max(UI.screenHeight / 1.25f, 675));

		public override void Notify_ClickOutsideWindow()
		{
			base.Notify_ClickOutsideWindow();
			Close();
		}

		public override void DoWindowContents(Rect inRect)
		{
			Rect labelRect = inRect;
			labelRect.height = 22;
			Color color = success ? ColorLibrary.Green : ColorLibrary.LogError;
			Widgets.Label(labelRect, "PatchSuccess".Translate(success.ToStringYesNo().Colorize(color)));

			Rect beforeRect = inRect;
			beforeRect.height -= (labelRect.height + 2);
			beforeRect.y += (labelRect.height + 2);
			beforeRect.width = inRect.width / 2 - 5;
			DrawBeforePatch(beforeRect);

			Rect afterRect = beforeRect;
			afterRect.x += beforeRect.width + 5;
			DrawAfterPatch(afterRect);
		}

		private void DrawBeforePatch(Rect rect)
		{
			Rect labelRect = rect;

			var font = Text.Font;
			Text.Font = GameFont.Medium;
			string label = "XmlPatchBefore".Translate();
			float labelHeight = Text.CalcHeight(label, labelRect.width);
			labelRect.height = labelHeight;
			Widgets.Label(labelRect, label);
			Text.Font = font;

			rect.y += labelRect.height;
			rect.height -= labelRect.height;
			Widgets.DrawMenuSection(rect);
			rect = rect.ContractedBy(5);
			XmlPatchConsole.TextAreaScrollable(rect, before, ref beforeScrollPosition);
		}

		private void DrawAfterPatch(Rect rect)
		{
			Rect labelRect = rect;

			var font = Text.Font;
			Text.Font = GameFont.Medium;
			string label = "XmlPatchAfter".Translate();
			float labelHeight = Text.CalcHeight(label, labelRect.width);
			labelRect.height = labelHeight;
			Widgets.Label(labelRect, label);
			Text.Font = font;

			rect.y += labelRect.height;
			rect.height -= labelRect.height;
			Widgets.DrawMenuSection(rect);
			rect = rect.ContractedBy(5);
			XmlPatchConsole.TextAreaScrollable(rect, after, ref afterScrollPosition);
		}

		private bool ValidateXml()
		{
			XmlDocument doc = new XmlDocument();
			doc.AppendChild(doc.CreateElement("ValidateXml"));
			foreach (XmlNode node in parents)
			{
				try
				{
					doc.DocumentElement.AppendChild(doc.ImportNode(node, true));
				}
				catch (XmlException)
				{
					return false;
				}
			}
			return true;
		}
	}
}
