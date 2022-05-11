using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using HarmonyLib;
using UnityEngine;

namespace XmlPatchHelper
{
	public class XmlPatchModSettings : ModSettings
	{
		public static Color LightBlue => new Color(0.235f, 0.745f, 0.941f, 1);
		public static Color Salmon => new Color(1, 0.58f, 0.451f, 1);
		public static Color Pink => new Color(0.89f, 0.408f, 0.827f, 1);
		public static Color White => new Color(1, 1, 1, 1);
		public static Color Comment => new Color(0, 0.588f, 0.196f, 1);

		public Color nodeColor = LightBlue;
		public Color attributeNameColor = Salmon;
		public Color attributeValueColor = Pink;
		public Color textColor = White;
		public Color commentColor = Comment;

		public override void ExposeData()
		{
			Scribe_Values.Look(ref nodeColor, nameof(nodeColor), LightBlue, true);
			Scribe_Values.Look(ref attributeNameColor, nameof(attributeNameColor), Salmon, true);
			Scribe_Values.Look(ref attributeValueColor, nameof(attributeValueColor), Pink, true);
			Scribe_Values.Look(ref textColor, nameof(textColor), White, true);
			Scribe_Values.Look(ref commentColor, nameof(commentColor), Comment, true);
		}
	}

	public class XmlPatchMod : Mod
	{
		public static XmlPatchMod mod;
		public static XmlPatchModSettings settings;

		private static XmlSection highlightedSection;
		private static bool highlightedAtAll;

		public XmlPatchMod(ModContentPack content) : base(content)
		{
			mod = this;
			settings = GetSettings<XmlPatchModSettings>();
			highlightedSection = XmlSection.None;
		}

		public string Open(Color color) => $"<<i></i>{"XmlNode".Translate()}  ".Colorize(color);
		public string Name(Color color) => $" {"XmlAttributeName".Translate()}".Colorize(color);
		public string Equals(Color color) => " = ".Colorize(color);
		public string Value(Color color) => $"\"{"XmlAttributeValue".Translate()}\"".Colorize(color);
		public string InnerText(Color color) => "XmlInnerText".Translate().Colorize(color);
		public string CloseOpen(Color color) => ">".Colorize(color);
		public string Close(Color color) => $"<<i></i>/{"XmlNode".Translate()}>  ".Colorize(color);
		public string Comment(Color color) => $"<<i></i>!-- {"XmlComment".Translate()} --<i></i>>  ".Colorize(color);

		public override void DoSettingsWindowContents(Rect inRect)
		{
			base.DoSettingsWindowContents(inRect);

			var richText = Text.CurFontStyle.richText;

			Color nodeColor = settings.nodeColor;
			Color attributeNameColor = settings.attributeNameColor;
			Color attributeValueColor = settings.attributeValueColor;
			Color textColor = settings.textColor;
			Color commentColor = settings.commentColor;

			highlightedAtAll = false;

			float textHeight = Text.CalcHeight("Test", inRect.width);

			Rect colorRect = new Rect(inRect);
			colorRect.height = textHeight * 2 + 2;
			float totalWidth = Text.CalcSize(Open(Color.white) + Name(Color.white) + Equals(Color.white) + Value(Color.white) + CloseOpen(Color.white) + InnerText(Color.white) + Close(Color.white)).x;
			colorRect.width = totalWidth + 10;
			Widgets.DrawMenuSection(colorRect);
			colorRect.width = inRect.width;
			colorRect.height = textHeight;
			colorRect.y += 1;
			colorRect.x += 5;
			Rect commentRect = colorRect;
			colorRect.y += colorRect.height;

			DrawColoredText(ref commentRect, Comment, commentColor, settings.commentColor, XmlSection.Comment, (Color color) => settings.commentColor = color);
			DrawColoredText(ref colorRect, Open, nodeColor, settings.nodeColor, XmlSection.Node, (Color color) => settings.nodeColor = color);
			DrawColoredText(ref colorRect, Name, attributeNameColor, settings.attributeNameColor, XmlSection.AttributeName, (Color color) => settings.attributeNameColor = color);
			DrawColoredText(ref colorRect, Equals, textColor, settings.textColor, XmlSection.InnerText, null, false);
			DrawColoredText(ref colorRect, Value, attributeValueColor, settings.attributeValueColor, XmlSection.AttributeValue, (Color color) => settings.attributeValueColor = color);
			DrawColoredText(ref colorRect, CloseOpen, nodeColor, settings.nodeColor, XmlSection.Node, (Color color) => settings.nodeColor = color);
			DrawColoredText(ref colorRect, InnerText, textColor, settings.textColor, XmlSection.InnerText, (Color color) => settings.textColor = color);
			DrawColoredText(ref colorRect, Close, nodeColor, settings.nodeColor, XmlSection.Node, (Color color) => settings.nodeColor = color);

			if (!highlightedAtAll)
			{
				highlightedSection = XmlSection.None;
			}
		}

		private static void DrawColoredText(ref Rect rect, Func<Color, string> text, Color color, Color originalColor, XmlSection section, Action<Color> setColor, bool editable = true)
		{
			var richText = Text.CurFontStyle.richText;
			string colorizedText = text(color);
			float length = Text.CalcSize(colorizedText).x;
			rect.width = length;
			if (editable)
			{
				if (Widgets.ButtonInvisible(rect))
				{
					Find.WindowStack.Add(new Dialog_ColorPicker(originalColor, setColor));
				}

				bool mouseOver = Mouse.IsOver(rect);
				if (mouseOver || highlightedSection == section)
				{
					if (mouseOver)
					{
						highlightedSection = section;
						highlightedAtAll = true;
					}
					if (color.r <= 0.5f && color.g <= 0.5f && color.b <= 0.5f)
					{
						color.r += 0.25f;
						color.g += 0.25f;
						color.b += 0.25f;
					}
					else
					{
						color.r -= 0.25f;
						color.g -= 0.25f;
						color.b -= 0.25f;
					}
					colorizedText = text(color);
					length = Text.CalcSize(colorizedText).x;
				}
			}
			Widgets.Label(rect, colorizedText);
			rect.x += length;
		}

		public override void WriteSettings()
		{
			base.WriteSettings();
			Find.WindowStack.TryRemove(typeof(Dialog_ColorPicker));
		}

		public override string SettingsCategory()
		{
			return $"XmlPatchHelper".Translate();
		}

		private enum XmlSection
		{
			None,
			Node,
			AttributeName,
			AttributeValue,
			InnerText,
			Comment
		}
	}
}
