using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Verse;
using UnityEngine;

namespace XmlPatchHelper
{
	[StaticConstructorOnStartup]
	public static class XmlText
	{
		public static bool draggingCP;
		public static bool draggingHue;
		public static bool draggingDisplacement;

		public static string[] RichTextBrackets = { "color", "i", "b", "size", "material", "quad" };

		public static Texture2D ColorChart = new Texture2D(255, 255);
		public static Texture2D HueChart = new Texture2D(1, 255);

		public static Color Blackist = new Color(0.06f, 0.06f, 0.06f);
		public static Color Greyist = new Color(0.2f, 0.2f, 0.2f);

		public static readonly Texture2D ColorPicker = ContentFinder<Texture2D>.Get("ColorPicker/ColorCog");
		public static readonly Texture2D ColorHue = ContentFinder<Texture2D>.Get("ColorPicker/ColorHue");

		static XmlText()
		{
			for (int i = 0; i < 255; i++)
			{
				HueChart.SetPixel(0, i, Color.HSVToRGB(Mathf.InverseLerp(0f, 255f, i), 1f, 1f));
			}
			HueChart.Apply(false);
			for (int j = 0; j < 255; j++)
			{
				for (int k = 0; k < 255; k++)
				{
					Color color = Color.clear;
					Color c = Color.Lerp(color, Color.white, Mathf.InverseLerp(0f, 255f, j));
					color = Color32.Lerp(Color.black, c, Mathf.InverseLerp(0f, 255f, k));
					ColorChart.SetPixel(j, k, color);
				}
			}
			ColorChart.Apply(false);
		}

		public static string Text(string text, int tabs = 0)
		{
			string prepend = string.Empty;
			for (int i = 0; i < tabs; i++)
			{
				prepend += "\t";
			}
			return prepend + text.Colorize(XmlPatchMod.settings.textColor);
		}

		public static string OpenBracket(string node, int tabs = 0, XmlAttributeCollection attributes = null)
		{
			List<(string, string)> attributeList = new List<(string, string)>();
			if (attributes != null)
			{
				foreach (XmlAttribute attribute in attributes)
				{
					attributeList.Add((attribute.Name, attribute.Value));
				}
			}
			return OpenBracket(node, tabs, attributeList.ToArray());
		}

		public static string OpenSelfClosingBracket(string node, int tabs = 0, XmlAttributeCollection attributes = null)
		{
			List<(string, string)> attributeList = new List<(string, string)>();
			if (attributes != null)
			{
				foreach (XmlAttribute attribute in attributes)
				{
					attributeList.Add((attribute.Name, attribute.Value));
				}
			}
			return OpenSelfClosingBracket(node, tabs, attributeList.ToArray());
		}

		public static string OpenBracket(string node, int tabs = 0, params (string name, string value)[] attribute)
		{
			string prepend = string.Empty;
			for (int i = 0; i < tabs; i++)
			{
				prepend += "\t";
			}
			if (RichTextBrackets.Contains(node.ToLowerInvariant()))
			{
				node = $"<i></i>{node}";
			}
			string bracket = $"<{node}";
			if (!attribute.NullOrEmpty())
			{
				foreach ((string name, string value) in attribute)
				{
					bracket += $" {AttributeName(name)}{Text(" = ")}{AttributeValue(value)}";
				}
			}
			bracket += ">";
			return prepend + bracket.Colorize(XmlPatchMod.settings.nodeColor);
		}

		public static string OpenSelfClosingBracket(string node, int tabs = 0, params (string name, string value)[] attribute)
		{
			string prepend = string.Empty;
			for (int i = 0; i < tabs; i++)
			{
				prepend += "\t";
			}
			if (RichTextBrackets.Contains(node.ToLowerInvariant()))
			{
				node = $"<i></i>{node}";
			}
			string bracket = $"<{node}";
			if (!attribute.NullOrEmpty())
			{
				foreach ((string name, string value) in attribute)
				{
					bracket += $" {AttributeName(name)}{Text(" = ")}{AttributeValue(value)}";
				}
			}
			bracket += "/<i></i>>";
			return prepend + bracket.Colorize(XmlPatchMod.settings.nodeColor);
		}

		public static string CloseBracket(string node, int tabs = 0)
		{
			string prepend = string.Empty;
			for (int i = 0; i < tabs; i++)
			{
				prepend += "\t";
			}
			if (RichTextBrackets.Contains(node.ToLowerInvariant()))
			{
				node = $"<i></i>{node}";
			}
			return prepend + $"<<i></i>/{node}>".Colorize(XmlPatchMod.settings.nodeColor);
		}

		public static string AttributeName(string name)
		{
			return name.Colorize(XmlPatchMod.settings.attributeNameColor);
		}

		public static string AttributeValue(string value)
		{
			return $"\"{value}\"".Colorize(XmlPatchMod.settings.attributeValueColor);
		}

		public static string Comment(string comment, int tabs = 0)
		{
			string prepend = string.Empty;
			for (int i = 0; i < tabs; i++)
			{
				prepend += "\t";
			}
			return prepend + $"<!-- {comment} -->".Colorize(XmlPatchMod.settings.commentColor);
		}

		public static Rect DrawColorPicker(Rect fullRect, ref float hue, ref float saturation, ref float value, Action<float, float, float> colorSetter)
		{
			Rect rect = fullRect.ContractedBy(10f);
			rect.width = 15f;

			if (Input.GetMouseButtonDown(0) && Mouse.IsOver(rect) && !draggingHue)
			{
				draggingHue = true;
			}
			if (draggingHue && Event.current.isMouse)
			{
				float num = hue;
				hue = Mathf.InverseLerp(rect.height, 0f, Event.current.mousePosition.y - rect.y);
				if (hue != num)
				{
					colorSetter(hue, saturation, value);
				}
			}
			if (Input.GetMouseButtonUp(0))
			{
				draggingHue = false;
			}
			Widgets.DrawBoxSolid(rect.ExpandedBy(1f), Color.grey);
			Widgets.DrawTexturePart(rect, new Rect(0f, 0f, 1f, 1f), HueChart);
			Rect rect2 = new Rect(0f, 0f, 16f, 16f)
			{
				center = new Vector2(rect.center.x, rect.height * (1f - hue) + rect.y).Rounded()
			};

			Widgets.DrawTextureRotated(rect2, ColorHue, 0f);
			rect = fullRect.ContractedBy(10f);
			rect.x = rect.xMax - rect.height;
			rect.width = rect.height;
			if (Input.GetMouseButtonDown(0) && Mouse.IsOver(rect) && !draggingCP)
			{
				draggingCP = true;
			}
			if (draggingCP)
			{
				saturation = Mathf.InverseLerp(0f, rect.width, Event.current.mousePosition.x - rect.x);
				value = Mathf.InverseLerp(rect.width, 0f, Event.current.mousePosition.y - rect.y);
				colorSetter(hue, saturation, value);
			}
			if (Input.GetMouseButtonUp(0))
			{
				draggingCP = false;
			}
			Widgets.DrawBoxSolid(rect.ExpandedBy(1f), Color.grey);
			Widgets.DrawBoxSolid(rect, Color.white);
			GUI.color = Color.HSVToRGB(hue, 1f, 1f);
			Widgets.DrawTextureFitted(rect, ColorChart, 1f);
			GUI.color = Color.white;
			GUI.BeginClip(rect);
			rect2.center = new Vector2(rect.width * saturation, rect.width * (1f - value));
			if (value >= 0.4f && (hue <= 0.5f || saturation <= 0.5f))
			{
				GUI.color = Blackist;
			}
			Widgets.DrawTextureFitted(rect2, ColorPicker, 1f);
			GUI.color = Color.white;
			GUI.EndClip();
			return rect;
		}
	}
}
