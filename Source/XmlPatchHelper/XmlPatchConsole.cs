using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.XPath;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;
using HarmonyLib;

namespace XmlPatchHelper
{
    public class XmlPatchConsole : Window
    {
		private const int MaxInnerTextLength = 25;

		private const string XPathFieldName = "xpath";
		private static Vector2 xmlScrollPosition;
		private static Vector2 fieldsScrollPosition;
		private static Vector2 outputScrollPosition;

		private static float offset = 10;

		private static Regex predicateMatch = new Regex("^[a-zA-Z0-9_]+\\[[a-zA-Z0-9_]+ *= *\"[a-zA-Z0-9_)]+\"\\]");
		private static XmlDocument document;
		private static Stopwatch stopwatch;

		private static StringBuilder summary;
		private static Dictionary<FieldInfo, StringBuilder> containerSummaries;

		private static Dictionary<Type, HashSet<string>> excludedFields = new Dictionary<Type, HashSet<string>>();

		private static Dictionary<Type, List<FieldInfo>> patchTypes = new Dictionary<Type, List<FieldInfo>>();
		private static List<PatchOperation> activeOperations = new List<PatchOperation>();

		private static Type patchType;
		private static PatchOperation patchOperation;

		private static int sampleSize = 1000;
		private static string sampleBuffer = sampleSize.ToStringSafe();

		private static string xpath;
		private static int nodesPerMatch;
		private static int depth;
		private static int tabs;

		private static float fieldScrollableHeight;

		private static SimpleCurve cutoutSampleSizeCurve = new SimpleCurve(new List<CurvePoint>()
		{
			new CurvePoint(1, 10000),
			new CurvePoint(10, 5000),
			new CurvePoint(100, 500),
			new CurvePoint(1000, 100),
			new CurvePoint(2000, 10),
			new CurvePoint(3000, 1)
		});

		public XmlPatchConsole()
		{
			GenerateCombinedXmlDoc(document == null);

			closeOnAccept = false;
			stopwatch = new Stopwatch();
			summary = new StringBuilder();
			containerSummaries = new Dictionary<FieldInfo, StringBuilder>();

			closeOnClickedOutside = true;
			doCloseX = true;
		}

		public override Vector2 InitialSize => new Vector2(UI.screenWidth / 1.05f, UI.screenHeight / 1.2f);

		public bool GeneratingXmlDoc { get; private set; } = false;

		public static void ValidateFieldScrollableHeight()
		{
			
		}

		public static void ExcludeField(Type declaringType, string name, bool logNotFound = true)
		{
			FieldInfo field = AccessTools.Field(declaringType, name);
			if (field == null)
			{
				if (logNotFound)
				{
					Log.Error($"Unable to find {declaringType}.{name} field for excluding.");
				}
				return;
			}
			if (!excludedFields.ContainsKey(declaringType))
			{
				excludedFields.Add(declaringType, new HashSet<string>());
			}
			excludedFields[declaringType].Add(name);
		}

		public static bool Excluded(FieldInfo field)
		{
			if (excludedFields.TryGetValue(field.DeclaringType, out HashSet<string> names))
			{
				return names.Contains(field.Name);
			}
			return false;
		}

		public override void DoWindowContents(Rect inRect)
		{
			if (GeneratingXmlDoc)
			{
				return;
			}

			var font = Text.Font;
			Text.Font = GameFont.Small;

			Rect leftWindow = new Rect(0, 0, inRect.width / 1.75f - offset, inRect.height);
			DoXmlArea(leftWindow);
			Rect topRightWindow = new Rect(leftWindow.width + offset * 2, leftWindow.y + Dialog_ColorPicker.ButtonHeight + 2, inRect.width - leftWindow.width - offset * 2, inRect.height);
			DoFieldsArea(topRightWindow);
			Text.Font = font;
		}

		private void DoXmlArea(Rect rect)
		{
			Rect buttonRect = new Rect(0, 0, rect.width / 5 - 1, Dialog_ColorPicker.ButtonHeight);
			if (Widgets.ButtonText(buttonRect, "RunXPath".Translate()))
			{
				UpdateSelect();
			}
			buttonRect.x += buttonRect.width + 1;
			if (Widgets.ButtonText(buttonRect, "XPathProfile".Translate()))
			{
				LongEventHandler.QueueLongEvent(delegate ()
				{
					ProfileSelect(sampleSize, cutoutSampleSizeCurve);
				}, "XPathProfiling", true, null);
			}
			buttonRect.x += buttonRect.width + 1;
			if (Widgets.ButtonText(buttonRect, "RegenerateXmlDoc".Translate()))
			{
				GenerateCombinedXmlDoc(false);
				return;
			}
			buttonRect.x += buttonRect.width + 1;
			if (Widgets.ButtonText(buttonRect, "DownloadMatches".Translate()))
			{
				string filePath = Path.Combine(Application.persistentDataPath, "XmlPatchHelper_XPathResult.xml");
				try
				{
					XmlNodeList nodeList = document.SelectNodes(xpath);
					XmlDocument matchDoc = new XmlDocument();
					matchDoc.AppendChild(matchDoc.CreateElement("XPathMatches"));
					foreach (XmlNode node in nodeList)
					{
						matchDoc.DocumentElement.AppendChild(matchDoc.ImportNode(node, true));
					}

					matchDoc.Save(filePath);
					Application.OpenURL(filePath);
				}
				catch (Exception ex)
				{
					Log.Error($"Unable to download XmlDocument to {filePath} Exception = {ex}");
				}
			}
			buttonRect.x += buttonRect.width + 1;
			if (Widgets.ButtonText(buttonRect, "RunPatchOperation".Translate()))
			{
				try
				{
					document.SelectNodes(xpath);
					ShowPatchResults();
				}
				catch (XPathException)
				{
					Messages.Message("InvalidXPathException".Translate(), MessageTypeDefOf.RejectInput);
				}
				catch (Exception ex)
				{
					Log.Error($"Exception thrown while simulating xpath patch operation. Ex = {ex}");
				}
			}
			rect.y = buttonRect.height + 2;
			rect.height -= buttonRect.height + 2;
			Widgets.DrawMenuSection(rect);
			rect = rect.ContractedBy(5);
			TextAreaScrollable(rect, summary.ToString(), ref xmlScrollPosition);
			float sampleBoxSize = rect.width / 2.5f;
			Rect sampleBoxRect = new Rect(rect)
			{
				x = buttonRect.x + buttonRect.width + 1,
				y = buttonRect.y,
				width = sampleBoxSize,
				height = 24
			};
			Widgets.TextFieldNumericLabeled(sampleBoxRect, "ProfileSampleSize".Translate(), ref sampleSize, ref sampleBuffer, max: 3000);
		}

		private void DoFieldsArea(Rect rect)
		{
			Rect fieldRect = rect;
			float dropdownHeight = 24;
			fieldRect.height = dropdownHeight;
			Widgets.Label(fieldRect, "PatchOperationType".Translate());
			fieldRect.x += Text.CalcSize("PatchOperationType".Translate()).x + 5;
			fieldRect.width = rect.width / 2;
			Widgets.Dropdown(fieldRect, patchType, (Type type) => type, PatchTypeSelection_GenerateMenu, patchType.Name);
			fieldRect = rect;
			fieldRect.y += dropdownHeight + 2;

			if (patchType != null && patchOperation != null)
			{
				//Widgets.BeginScrollView(fieldRect, ref fieldsScrollPosition, fieldRect);
				foreach (FieldInfo fieldInfo in patchTypes[patchType])
				{
					InputField(ref fieldRect, fieldInfo.Name, fieldInfo, (object value) =>
					{
						if (fieldInfo.Name == XPathFieldName)
						{
							xpath = (string)value;
							UpdateSelect();
						}
						if (value is XmlContainer container)
						{
							containerSummaries[fieldInfo].Clear();
							AppendXmlRecursive(container.node, false, containerSummaries[fieldInfo]);
						}
					});
					fieldRect.y += fieldRect.height + 5;
				}
				//Widgets.EndScrollView();
			}
		}

		private void ShowPatchResults()
		{
			//XmlDocument compareDoc = 
		}

		/// <summary>
		/// Update selection given <see cref="xpath"/>
		/// </summary>
		private void UpdateSelect()
		{
			summary.Clear();
			try
			{
				stopwatch.Restart();
				XmlNodeList nodeList = document.SelectNodes(xpath);
				stopwatch.Stop();

				BuildXmlSummary(nodeList, summary);
				
				summary.PrependLine();

				if (xpath.StartsWith("/Defs"))
				{
					summary.PrependLine("SummaryStarterSuggestion".Translate(Environment.NewLine).Colorize(Color.yellow));
					summary.PrependLine();
				}

				summary.PrependLine(XmlText.Comment("SummaryExecutionTime".Translate(string.Format("{0:0.##}", stopwatch.ElapsedTicks), string.Format("{0:0.##}", stopwatch.ElapsedMilliseconds))));
				string summaryText = nodeList.Count >= 5 ? "SummaryMatchCountWithDisclaimer".Translate(nodeList.Count) : "SummaryMatchCount".Translate(nodeList.Count);
				summary.PrependLine(XmlText.Comment(summaryText));

				if (nodeList.Count == 0)
				{
					//Attempt auto-correct
					try
					{
						string[] xpathTree = Regex.Split(xpath, @"(\/{1,2})");
						string lastSelect = xpathTree.LastOrDefault();
						if (predicateMatch.IsMatch(lastSelect))
						{
							string[] lastSelectPieceMeal = Regex.Split(lastSelect, @"(?=[\[])");
							lastSelectPieceMeal[0] = "*"; //wild card match
							xpathTree[xpathTree.Length - 1] = string.Join("", lastSelectPieceMeal);
							string correctedXPath = string.Join("", xpathTree);
							XmlNodeList correctionAttempt = document.SelectNodes(correctedXPath);
							if (correctionAttempt.Count > 0)
							{
								summary.AppendLine();
								summary.AppendLine("CloseSearchSuggestion".Translate());
								foreach (XmlNode node in correctionAttempt)
								{
									lastSelectPieceMeal[0] = node.Name; //Substitute wildcard match
									xpathTree[xpathTree.Length - 1] = string.Join("", lastSelectPieceMeal);
									correctedXPath = string.Join("", xpathTree);
									summary.AppendLine(correctedXPath.Colorize(ColorLibrary.Grey));
								}
							}
						}
					}
					catch
					{
					}
				}
			}
			catch (XPathException)
			{
				summary.Clear();
				summary.Append("InvalidXPathException".Translate().Colorize(ColorLibrary.LogError));
			}
			summary.AppendLine();
		}

		private double ProfileSelect(int sample, SimpleCurve sampleSizeForAverage)
		{
			summary.Clear();
			try
			{
				summary.AppendLine("ProfileMatchHeader".Translate(xpath));
				summary.AppendLine("ProfileAttemptingSampleSize".Translate(sample));
				List<long> results = new List<long>();
				List<long> ms = new List<long>();
				XmlNodeList nodeList = null;

				for (int i = 0; i < sample; i++)
				{
					XmlDocument profileDocument = new XmlDocument();
					profileDocument.LoadXml(document.OuterXml);
					stopwatch.Restart();
					nodeList = profileDocument.SelectNodes(xpath);
					stopwatch.Stop();
					results.Add(stopwatch.ElapsedTicks);
					ms.Add(stopwatch.ElapsedMilliseconds);
					//Only check for exit point if given enough sample points to determine if operation is too slow
					if (results.Average() >= cutoutSampleSizeCurve.Evaluate(i))
					{
						summary.AppendLine("ProfileExitOperationTooLong".Translate(i));
						break;
					}
				}
				double average = results.Average();
				double averageMS = ms.Average();
				summary.AppendLine("ProfileMatchResults".Translate(nodeList.Count));
				summary.AppendLine("ProfileAverageResults".Translate(string.Format("{0:0.##}", average), string.Format("{0:0.##}", averageMS)));
				summary.AppendLine();
				summary.AppendLine("ProfileTickDisclaimer".Translate());
				return average;
			}
			catch (XPathException)
			{
				summary.Clear();
				summary.Append("InvalidXPathException".Translate().Colorize(ColorLibrary.LogError));
			}
			return 0;
		}

		private static void BuildXmlSummary(XmlNodeList nodeList, StringBuilder summary, int maxNodeCount = 50)
		{
			int i = 0;
			bool truncate = nodeList.Count > 1 || (nodeList.Count == 1 && (nodeList[0].ChildNodes.Count > maxNodeCount || nodeList[0].Name == "#document"));
			foreach (XmlNode node in nodeList)
			{
				depth = 0;
				tabs = 0;
				nodesPerMatch = 0;
				AppendXmlRecursive(node, truncate, summary);
				if (i >= 5)
				{
					break;
				}
				i++;
				summary.AppendLine();
			}
		}

		public static bool AppendXmlRecursive(XmlNode node, bool truncateXml, StringBuilder summary, int depth = 5, int nodesPerMatch = 10)
		{
			XmlAttributeCollection attributes = node.Attributes;
			if (node.IsEmpty())
			{
				summary.AppendLine(XmlText.OpenSelfClosingBracket(node.Name, tabs, attributes));
				return true;
			}
			bool truncate = false;
			if (node.IsTextOnly())
			{
				summary.AppendLine(XmlText.Text(node.InnerText, tabs));
				return true;
			}
			summary.Append(XmlText.OpenBracket(node.Name, tabs, attributes));
			if (node.IsTextElement())
			{
				summary.Append(XmlText.Text(node.InnerText.TruncateString(MaxInnerTextLength)));
				summary.AppendLine(XmlText.CloseBracket(node.Name));
				return true;
			}
			else if (node.HasChildNodes)
			{
				summary.AppendLine();
				tabs++;
				if (truncateXml && XmlPatchConsole.depth > depth)
				{
					summary.AppendLine(XmlText.Comment("...", tabs));
					tabs--;
					return false;
				}
				XmlPatchConsole.depth++;
				foreach (XmlNode childNode in node)
				{
					if (truncateXml && XmlPatchConsole.nodesPerMatch >= nodesPerMatch)
					{
						truncate = true;
						break;
					}
					if (!AppendXmlRecursive(childNode, truncateXml, summary))
					{
						break;
					}
					XmlPatchConsole.nodesPerMatch++;
				}
				if (truncate)
				{
					summary.AppendLine(XmlText.Comment("...", tabs));
				}
				tabs--;
			}
			summary.AppendLine(XmlText.CloseBracket(node.Name, tabs));
			return true;
		}

		public void GenerateCombinedXmlDoc(bool freshGen)
		{
			List<LoadableXmlAsset> xmls = new List<LoadableXmlAsset>();
			var assetLookup = new Dictionary<XmlNode, LoadableXmlAsset>();
			LongEventHandler.QueueLongEvent(delegate ()
			{
				GeneratingXmlDoc = true;
				foreach (ModContentPack modContentPack in LoadedModManager.RunningMods)
				{
					xmls.AddRange(DirectXmlLoader.XmlAssetsInModFolder(modContentPack, "Defs/", null).ToList());
				}
				document = LoadedModManager.CombineIntoUnifiedXML(xmls, assetLookup);
				
				if (freshGen)
				{
					patchTypes = new Dictionary<Type, List<FieldInfo>>();
					activeOperations = new List<PatchOperation>();
					foreach (Type type in typeof(PatchOperationPathed).AllSubclassesNonAbstract())
					{
						patchTypes[type] = new List<FieldInfo>();
						activeOperations.Add((PatchOperationPathed)Activator.CreateInstance(type));
						foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(field => !Excluded(field))
																																		.OrderBy(field => field.FieldType == typeof(XmlContainer))
																																		.ThenByDescending(field => field.Name))
						{
							patchTypes[type].Add(field);
						}
					}
				}
				patchType ??= patchTypes.FirstOrDefault().Key;
				patchOperation ??= activeOperations.FirstOrDefault(po => po.GetType() == patchType);
				foreach (FieldInfo fieldInfo in patchTypes[patchType])
				{
					if (fieldInfo.FieldType == typeof(XmlContainer))
					{
						containerSummaries[fieldInfo] = new StringBuilder();
						if (fieldInfo.GetValue(patchOperation) is XmlContainer container)
						{
							if (!container?.node?.IsEmpty() ?? false)
							{
								AppendXmlRecursive(container.node, false, containerSummaries[fieldInfo]);
							}
						}
					}
				}
				GeneratingXmlDoc = false;
			}, "FetchingXmlDefs", true, (Exception ex) =>
			{
				Log.Error($"Exception thrown while attempting to generate combiend xml doc.");
				GeneratingXmlDoc = false;
				Close(false);
			});
		}

		private static void InputField(ref Rect rect, string label, FieldInfo field, Action<object> onValueChange = null)
		{
			object value = field.GetValue(patchOperation);
			Rect inputRect = new Rect(rect);
			if (field.FieldType == typeof(string))
			{
				inputRect.height = 24;
				string leftBracket = XmlText.OpenBracket(label);
				string rightBracket = XmlText.CloseBracket(label);
				float lbrWidth = Text.CalcSize(leftBracket).x;
				float rbrWidth = Text.CalcSize(rightBracket).x;
				Widgets.LabelFit(inputRect, leftBracket);
				inputRect.y += inputRect.height;
				string newValue = TextArea(inputRect, (string)value, true, null, TextAnchor.MiddleLeft);
				if (newValue != (string)value)
				{
					value = newValue;
					onValueChange?.Invoke(value);
				}
				inputRect.y += inputRect.height;
				Widgets.LabelFit(inputRect, rightBracket);
			}
			else if (field.FieldType == typeof(int) || field.FieldType == typeof(float))
			{
				rect.height = 24;
				int intVal = (int)value;
				string leftBracket = XmlText.OpenBracket(label);
				string rightBracket = XmlText.CloseBracket(label);
				string buffer = string.Empty;
				float lbrWidth = Text.CalcSize(leftBracket).x;
				float rbrWidth = Text.CalcSize(rightBracket).x;
				float inputWidth = rect.width - lbrWidth - rbrWidth;
				Widgets.LabelFit(inputRect, leftBracket);
				inputRect.x += lbrWidth;
				Widgets.TextFieldNumeric(inputRect, ref intVal, ref buffer);
				if (!intVal.Equals(value))
				{
					value = intVal;
					onValueChange?.Invoke(value);
				}
				inputRect.x += Text.CalcSize(value.ToString()).x;
				Widgets.LabelFit(inputRect, rightBracket);
			}
			else if (field.FieldType.IsEnum)
			{
				inputRect.height = 24;
				string leftBracket = XmlText.OpenBracket(label);
				string rightBracket = XmlText.CloseBracket(label);
				float lbrWidth = Text.CalcSize(leftBracket).x;
				float rbrWidth = Text.CalcSize(rightBracket).x;
				Widgets.LabelFit(inputRect, leftBracket);
				inputRect.y += 24;
				inputRect.width = rect.width / 3;
				Widgets.Dropdown(inputRect, value.ToString(), (string name) => (int)Enum.Parse(field.FieldType, name, true), (string name) =>
				{
					List<Widgets.DropdownMenuElement<int>> elements = new List<Widgets.DropdownMenuElement<int>>();
					foreach (string enumName in Enum.GetNames(field.FieldType))
					{
						int enumValue = (int)Enum.Parse(field.FieldType, enumName);
						elements.Add(new Widgets.DropdownMenuElement<int>
						{
							option = new FloatMenuOption(enumName, delegate ()
							{
								field.SetValue(patchOperation, enumValue);
								onValueChange?.Invoke(enumValue);
							}),
							payload = enumValue
						});
					}
					return elements;
				}, value.ToString());
				inputRect.y += 24;
				Widgets.LabelFit(inputRect, rightBracket);
			}
			else if (field.FieldType == typeof(XmlContainer))
			{
				if (value == null)
				{
					value = new XmlContainer();
				}
				XmlContainer container = (XmlContainer)value;
				inputRect.height = 24;

				Rect buttonRect = inputRect;
				string containerButtonText = "Edit".Translate();
				buttonRect.width = Text.CalcSize(containerButtonText).x + 24;
				if (Widgets.ButtonText(buttonRect, containerButtonText))
				{
					Find.WindowStack.Add(new Dialog_AddNode(container, delegate (string node, string text, List<(string name, string value)> attributes)
					{
						XmlDocument doc = new XmlDocument();

						if (node.NullOrEmpty())
						{
							container.node = doc.CreateNode(XmlNodeType.Text, string.Empty, string.Empty);
							container.node.InnerText = text;
						}
						else
						{
							XmlElement element = doc.CreateElement(node);
							if (!attributes.NullOrEmpty())
							{
								foreach ((string name, string value) in attributes)
								{
									element.SetAttribute(name, value);
								}
							}
							element.InnerText = text;
							container.node = element;
						}
						onValueChange?.Invoke(value);
					}));
				}

				inputRect.y += buttonRect.height + 2;

				float summaryHeight = 0;
				float summaryY = inputRect.y;
				if (container.node.IsEmpty())
				{
					summaryHeight += inputRect.height;
					TextArea(inputRect, XmlText.OpenSelfClosingBracket(label));
				}
				else if (container.node.IsTextOnly())
				{
					summaryHeight += inputRect.height;
					string summary = containerSummaries[field].ToString().TrimEndNewlines();
					TextArea(inputRect, $"{XmlText.OpenBracket(label)}{summary}{XmlText.CloseBracket(label)}");
				}
				else
				{
					string valueOpen = XmlText.OpenBracket(label);
					string valueClose = XmlText.CloseBracket(label);

					string summary = containerSummaries[field].ToString().TrimEndNewlines();

					float originalX = inputRect.x;
					float tabWidth = Text.CalcSize("\t").x;
					summaryHeight += inputRect.height;
					TextArea(inputRect, valueOpen);
					inputRect.height = Text.CalcHeight(summary, inputRect.width);
					inputRect.y += Text.CalcHeight(valueOpen, inputRect.width);
					inputRect.x = rect.x + tabWidth;
					summaryHeight += inputRect.height;
					TextArea(inputRect, summary);
					inputRect.height = 24;
					inputRect.y += Text.CalcHeight(summary, inputRect.width);
					inputRect.x = originalX;
					summaryHeight += inputRect.height;
					TextArea(inputRect, valueClose);

					inputRect.height = summaryHeight;
					inputRect.y = summaryY;
				}

				value = container;
			}
			else
			{
				inputRect.height = 24;
				Widgets.LabelFit(inputRect, $"{label} (Unsupported field type = {field.FieldType})");
			}
			field.SetValue(patchOperation, value);
			rect.height = inputRect.height;
			rect.y = inputRect.y;
		}

		private static IEnumerable<Widgets.DropdownMenuElement<Type>> PatchTypeSelection_GenerateMenu(Type type)
		{
			foreach (Type registeredType in patchTypes.Keys)
			{
				yield return new Widgets.DropdownMenuElement<Type>
				{
					option = new FloatMenuOption(registeredType.Name, delegate ()
					{
						patchType = registeredType;
						patchOperation = activeOperations.FirstOrDefault(po => po.GetType() == patchType);
						containerSummaries = new Dictionary<FieldInfo, StringBuilder>();
						foreach (FieldInfo fieldInfo in patchTypes[patchType])
						{
							if (fieldInfo.FieldType == typeof(XmlContainer))
							{
								containerSummaries[fieldInfo] = new StringBuilder();
								if (fieldInfo.GetValue(patchOperation) is XmlContainer container)
								{
									if (!container?.node?.IsEmpty() ?? false)
									{
										AppendXmlRecursive(container.node, false, containerSummaries[fieldInfo]);
									}
								}
							}
						}
					}),
					payload = registeredType
				};
			}
		}

		public static string TextArea(Rect rect, string text, bool background = false, Regex validator = null, TextAnchor anchor = TextAnchor.UpperLeft)
		{
			var anchorOld = Text.Anchor;
			if (background)
			{
				Widgets.DrawMenuSection(rect);
				rect.x += 2;
			}
			Text.Anchor = anchor;
			string input = GUI.TextArea(rect, text, Text.CurFontStyle);
			Text.Anchor = anchorOld;
			if (validator == null || validator.IsMatch(input))
			{
				return input;
			}
			return text;
		}

		public static string TextField(Rect rect, string text, bool background = false, Regex validator = null, TextAnchor anchor = TextAnchor.UpperLeft)
		{
			var anchorOld = Text.Anchor;
			if (background)
			{
				Widgets.DrawMenuSection(rect);
				rect.x += 2;
			}
			Text.Anchor = anchor;
			string input = GUI.TextField(rect, text, Text.CurFontStyle);
			Text.Anchor = anchorOld;
			if (validator == null || validator.IsMatch(input))
			{
				return input;
			}
			return text;
		}

		public static string TextAreaScrollable(Rect rect, string text, ref Vector2 scrollbarPosition)
		{
			float width = rect.width - 16; //scrollbar space
			Rect rect2 = new Rect(0f, 0f, width, Mathf.Max(Text.CalcHeight(text, width) + 5, rect.height));
			Widgets.BeginScrollView(rect, ref scrollbarPosition, rect2);
			string result = TextArea(rect2, text);
			Widgets.EndScrollView();
			return result;
		}
	}
}
