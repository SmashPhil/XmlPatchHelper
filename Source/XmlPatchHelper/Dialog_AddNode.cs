using System;
using System.Collections.Generic;
using System.Xml;
using System.Text;
using RimWorld;
using Verse;
using Verse.Sound;
using UnityEngine;

namespace XmlPatchHelper
{
	public class Dialog_AddNode : Window
	{
		//Node - InnerText - (AttributeName, AttributeValue)
		private Action<string, string, List<(string name, string value)>> onAccept;

		private string node;
		private string text;
		private List<(string name, string value)> attributes;

		private bool nodeInvalid;
		private bool textInvalid;
		private List<int> attributesInvalid;

		private StringBuilder summary;
		private static Vector2 summaryScrollbarPosition;

		public Dialog_AddNode(XmlContainer container, Action<string, string, List<(string name, string value)>> onAccept, bool textOnly = false)
		{
			this.onAccept = onAccept;
			attributes = new List<(string name, string value)>();
			attributesInvalid = new List<int>();
			summary = new StringBuilder();
			if (!container?.node?.FirstChild?.IsEmpty() ?? false)
			{
				node = container.node.FirstChild.Name;
				if (node == "#text")
				{
					node = string.Empty;
				}
				text = container.node.FirstChild.InnerText;

				if (container.node.Attributes != null)
				{
					foreach (XmlAttribute attribute in container.node.Attributes)
					{
						attributes.Add((attribute.Name, attribute.Value));
					}
				}
			}
			RecacheSummary();

			closeOnCancel = true;
			closeOnAccept = false;
			doCloseX = true;
		}

		public override Vector2 InitialSize => new Vector2(700, 800);

		private bool ValidateXml()
		{
			XmlDocument doc = new XmlDocument();
			doc.AppendChild(doc.CreateElement("value"));
			try
			{
				try
				{
					nodeInvalid = false;

					if (!node.NullOrEmpty())
					{
						XmlElement element = doc.CreateElement(node);
						doc.DocumentElement.AppendChild(doc.ImportNode(element, true));
					}
				}
				catch (XmlException)
				{
					nodeInvalid = true;
				}

				try
				{
					textInvalid = false;

					XmlNode textNode = doc.CreateNode(XmlNodeType.Text, string.Empty, string.Empty);
					textNode.InnerText = text;
					if (node.NullOrEmpty())
					{
						doc.DocumentElement.AppendChild(doc.ImportNode(textNode, true));
					}
					else
					{
						doc.DocumentElement.FirstChild.AppendChild(doc.ImportNode(textNode, true));
					}
				}
				catch (XmlException)
				{
					textInvalid = true;
				}

				attributesInvalid.Clear();
				if (!node.NullOrEmpty() && attributes.Any())
				{
					for (int i = 0; i < attributes.Count; i++)
					{
						try
						{
							(string name, string value) = attributes[i];
							if (name.NullOrEmpty())
							{
								attributesInvalid.Add(i);
								continue;
							}
							(doc.DocumentElement.FirstChild as XmlElement).SetAttribute(name, value);
						}
						catch (XmlException)
						{
							attributesInvalid.Add(i);
						}
					}
				}
			}
			catch
			{
				nodeInvalid = true;
				textInvalid = true;
			}
			return !nodeInvalid && !textInvalid && !attributesInvalid.Any();
		}

		public override void Notify_ClickOutsideWindow()
		{
			base.Notify_ClickOutsideWindow();
			Close();
		}

		public void RecacheSummary()
		{
			summary.Clear();
			if (!node.NullOrEmpty())
			{
				summary.AppendLine(XmlText.OpenBracket("value"));
				{
					summary.Append(XmlText.OpenBracket(node, 1, attributes.ToArray()));
					{
						summary.Append(XmlText.Text(text));
					}
					summary.AppendLine(XmlText.CloseBracket(node));
				}
				summary.AppendLine(XmlText.CloseBracket("value"));
			}
			else
			{
				summary.Append(XmlText.OpenBracket("value"));
				{
					summary.Append(XmlText.Text(text));
				}
				summary.Append(XmlText.CloseBracket("value"));
			}
			if (!ValidateXml())
			{
				summary.Clear();
				summary.AppendLine("InvalidXmlException".Translate().Colorize(ColorLibrary.LogError));
			}
		}

		public override void DoWindowContents(Rect inRect)
		{
			Rect previewRect = new Rect(0, 0, inRect.width, inRect.height / 2);
			Widgets.Label(previewRect, "XmlPreview".Translate());
			previewRect.y += 24;
			Widgets.DrawMenuSection(previewRect);
			XmlPatchConsole.TextAreaScrollable(previewRect, summary.ToString(), ref summaryScrollbarPosition);

			Rect topRect = new Rect(0, previewRect.height + 26, inRect.width, inRect.height - Dialog_ColorPicker.ButtonHeight + 2);
			DrawOutput(topRect);

			Rect bottomRect = new Rect(0, inRect.height - Dialog_ColorPicker.ButtonHeight, inRect.width, Dialog_ColorPicker.ButtonHeight);
			Widgets.BeginGroup(bottomRect);
			{
				DrawBottomButtons(bottomRect);
			}
			Widgets.EndGroup();
		}

		private void DrawOutput(Rect rect)
		{
			var color = GUI.color;

			Rect inputRect = rect;
			inputRect.height = 24;
			bool dirty = false;

			float labelWidth = rect.width / 3;
			float fieldWidth = rect.width - labelWidth;

			inputRect.width = labelWidth;
			GUI.color = nodeInvalid ? ColorLibrary.LogError : Color.white;
			Widgets.Label(inputRect, "XmlNode".Translate());
			GUI.color = Color.white;
			inputRect.x = labelWidth;
			inputRect.width = fieldWidth;
			string changeNode = XmlPatchConsole.TextField(inputRect, node, true, anchor: TextAnchor.MiddleLeft);
			if (node != changeNode)
			{
				node = changeNode;
				dirty = true;
			}
			inputRect.x = rect.x;

			inputRect.y += 26;

			inputRect.width = labelWidth;
			GUI.color = textInvalid ? ColorLibrary.LogError : Color.white;
			Widgets.Label(inputRect, "XmlInnerText".Translate());
			GUI.color = Color.white;
			inputRect.x = labelWidth;
			inputRect.width = fieldWidth;
			string changeText = XmlPatchConsole.TextField(inputRect, text, true, anchor: TextAnchor.MiddleLeft);
			if (text != changeText)
			{
				text = changeText;
				dirty = true;
			}
			inputRect.x = rect.x;

			inputRect.y += 26;

			float buttonSpace = 24;
			float attributeLabelWidth = rect.width / 6 - buttonSpace;
			float attributeFieldWidth = (rect.width / 2) - attributeLabelWidth;
			int removalIndex = -1;
			
			for (int i = 0; i < attributes.Count; i++)
			{
				(string name, string value) = attributes[i];
				bool attributeInvalid = attributesInvalid.Contains(i);
				inputRect.width = buttonSpace;
				if (Widgets.ButtonText(inputRect, "-"))
				{
					removalIndex = i;
				}
				inputRect.x = buttonSpace + 2;
				inputRect.width = attributeLabelWidth;
				GUI.color = attributeInvalid ? ColorLibrary.LogError : Color.white;
				Widgets.Label(inputRect, "XmlAttributeName".Translate());
				GUI.color = Color.white;
				inputRect.x += attributeLabelWidth;
				inputRect.width = attributeFieldWidth - buttonSpace;
				string attributeNameChange = XmlPatchConsole.TextField(inputRect, name, true, anchor: TextAnchor.MiddleLeft);
				if (name != attributeNameChange)
				{
					name = attributeNameChange;
					dirty = true;
				}

				inputRect.x = rect.width / 2 + buttonSpace;
				inputRect.width = attributeLabelWidth;
				GUI.color = attributeInvalid ? ColorLibrary.LogError : Color.white;
				Widgets.Label(inputRect, "XmlAttributeValue".Translate());
				GUI.color = Color.white;
				inputRect.x += attributeLabelWidth;
				inputRect.width = attributeFieldWidth - buttonSpace;
				string attributeValueChange = XmlPatchConsole.TextField(inputRect, value, true, anchor: TextAnchor.MiddleLeft);
				if (value != attributeValueChange)
				{
					value = attributeValueChange;
					dirty = true;
				}

				attributes[i] = (name, value);

				inputRect.x = rect.x;
				inputRect.y += 26;
			}
			inputRect.width = Dialog_ColorPicker.ButtonWidth;
			if (attributes.Count < 10 && Widgets.ButtonText(inputRect, "XmlAddAttribute".Translate()))
			{
				attributes.Add((string.Empty, string.Empty));
				RecacheSummary();
			}

			if (removalIndex >= 0)
			{
				attributes.RemoveAt(removalIndex);
				dirty = true;
			}

			if (dirty)
			{
				RecacheSummary();
			}

			GUI.color = color;
		}

		private void DrawBottomButtons(Rect rect)
		{
			Rect buttonRect = new Rect(0, 0, Dialog_ColorPicker.ButtonWidth, rect.height);
			if (Widgets.ButtonText(buttonRect, "Accept".Translate()))
			{
				if (!ValidateXml())
				{
					RecacheSummary();
					SoundDefOf.ClickReject.PlayOneShotOnCamera(null);
					return;
				}
				onAccept(node, text, attributes);
				Close();
			}
			buttonRect.x += buttonRect.width + 1;
			if (Widgets.ButtonText(buttonRect, "Cancel".Translate()))
			{
				Close();
			}
		}
	}
}
