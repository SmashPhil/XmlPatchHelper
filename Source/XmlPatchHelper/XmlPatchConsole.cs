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
		private const float TotalAllowedProfilingTime = 15f;

		private const string XPathFieldName = "xpath";
		private static Vector2 xmlScrollPosition;
		private static Vector2 fieldsScrollPosition;

		private static float offset = 10;

		private static Regex predicateMatch = new Regex("^[a-zA-Z0-9_]+\\[[a-zA-Z0-9_]+ *= *\"[a-zA-Z0-9_)]+\"\\]");
		private static XmlDocument document;
		private static Stopwatch stopwatch;

		private static StringBuilder summary;
		private static Dictionary<FieldInfo, StringBuilder> containerSummaries;

		private static Dictionary<Type, HashSet<string>> excludedFields = new Dictionary<Type, HashSet<string>>();

		public static Dictionary<Type, List<FieldInfo>> patchTypes = new Dictionary<Type, List<FieldInfo>>();
		public static List<PatchOperation> activeOperations = new List<PatchOperation>();

		public static Type patchType;
		public static PatchOperation patchOperation;

		public static int sampleSize = 100;
		public static string sampleBuffer = sampleSize.ToStringSafe();

		public static string xpath;
		private static int nodesPerMatch;
		private static int depth;
		private static int tabs;

		private static float fieldScrollableHeight;

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

		public static XmlDocument CombinedXmlDoc => document;

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
			Rect rightWindow = new Rect(leftWindow.width + offset * 2, leftWindow.y, inRect.width - leftWindow.width - offset * 2, leftWindow.height);
			DoFieldsArea(rightWindow);
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
					ProfileSelect(sampleSize);
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
		}

		private void DoFieldsArea(Rect rect)
		{
			Rect fieldRect = rect;
			float dropdownHeight = 24;
			fieldRect.height = dropdownHeight;

			float sampleBoxSize = (Text.CalcSize("ProfileSampleSize".Translate()).x + 2) * 2;
			Rect sampleBoxRect = new Rect(fieldRect)
			{
				width = sampleBoxSize
			};
			Widgets.TextFieldNumericLabeled(sampleBoxRect, "ProfileSampleSize".Translate(), ref sampleSize, ref sampleBuffer, max: 3000);
			fieldRect.y += sampleBoxRect.height + 2;
			fieldRect.width = rect.width;
			Widgets.Label(fieldRect, "PatchOperationType".Translate());
			fieldRect.x += Text.CalcSize("PatchOperationType".Translate()).x + 5;
			fieldRect.width = rect.width / 2;
			Widgets.Dropdown(fieldRect, patchType, (Type type) => type, PatchTypeSelection_GenerateMenu, patchType.Name);
			fieldRect = rect;
			fieldRect.y += (dropdownHeight + 2) * 2;

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

			fieldRect.x += rect.width - Dialog_ColorPicker.ButtonWidth * 2;
			fieldRect.y = rect.height - 24;
			fieldRect.height = 24;
			fieldRect.width = Dialog_ColorPicker.ButtonWidth * 2;
			if (Widgets.ButtonText(fieldRect, "ExportPatchXml".Translate()))
			{
				try
				{
					document.SelectSingleNode(xpath);
				}
				catch (XPathException)
				{
					Log.Error($"Unable to export PatchOperation file. XPath is invalid.");
				}
				string filePath = Path.Combine(Application.persistentDataPath, "XmlPatchHelper_PatchOperation.xml");
				try
				{
					XmlNodeList nodeList = document.SelectNodes(xpath);
					XmlDocument exportDoc = new XmlDocument();
					exportDoc.AppendChild(exportDoc.CreateElement("Patch"));
					XmlElement operationElement = exportDoc.CreateElement("Operation");
					string patchOperationTypeValue = patchType.Namespace == "Verse" ? patchType.Name : $"{patchType.Namespace}.{patchType.Name}";
					operationElement.SetAttribute("Class", patchType.Name);

					foreach (FieldInfo field in patchTypes[patchType])
					{
						object value = field.GetValue(patchOperation);
						bool enumDefault = field.FieldType.IsEnum && (int)value == (int)field.FieldType.DefaultValue();
						if (value != field.FieldType.DefaultValue() && !enumDefault)
						{
							XmlElement fieldElement = exportDoc.CreateElement(field.Name);
							if (value is XmlContainer container)
							{
								fieldElement.AppendChild(exportDoc.ImportNode(container.node.FirstChild, true));
							}
							else
							{
								fieldElement.InnerText = value.ToStringSafe();
							}
							operationElement.AppendChild(exportDoc.ImportNode(fieldElement, true));
						}
					}
					exportDoc.DocumentElement.AppendChild(operationElement);
					exportDoc.Save(filePath);
					Application.OpenURL(filePath);
				}
				catch (Exception ex)
				{
					Log.Error($"Unable to export PatchOperation to {filePath} Exception = {ex}");
				}
			}
		}

		private void ShowPatchResults()
		{
			Find.WindowStack.Add(new Dialog_BeforeAfterPatch());
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

				StringBuilder prependedText = new StringBuilder();
				string summaryText = nodeList.Count >= XmlPatchMod.settings.resultsLimit ? "SummaryMatchCountWithDisclaimer".Translate(nodeList.Count, XmlPatchMod.settings.resultsLimit) : "SummaryMatchCount".Translate(nodeList.Count);
				prependedText.AppendLine(XmlText.Comment(summaryText));
				prependedText.AppendLine(XmlText.Comment("SummaryExecutionTime".Translate(string.Format("{0:0.##}", stopwatch.ElapsedTicks), string.Format("{0:0.##}", stopwatch.ElapsedMilliseconds))));

				if (xpath.StartsWith("/Defs"))
				{
					prependedText.AppendLine("SummaryStarterSuggestion".Translate(Environment.NewLine).Colorize(Color.yellow));
					prependedText.AppendLine();
				}

				AttemptBackTraversalCorrect(prependedText);
				if (nodeList.Count == 0)
				{
					AttemptDefTypeAutoCorrect(prependedText);
					AttemptDefNameToAttributeAutoCorrect(prependedText, ("defName", "@Name"), ("defName", "@ParentName"), ("@Name", "defName"), ("@ParentName", "defName"), ("ParentName", "@ParentName"), ("Name", "@Name"));
					AttemptDefNameCloseMatch(prependedText);
				}

				summary.PrependLine(prependedText.ToString());
			}
			catch (XPathException)
			{
				summary.Clear();
				summary.Append("InvalidXPathException".Translate().Colorize(ColorLibrary.LogError));
			}
			summary.AppendLine();
		}

		private static void AttemptDefTypeAutoCorrect(StringBuilder stringBuilder)
		{
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
						stringBuilder.AppendLine();
						stringBuilder.AppendLine("CloseSearchSuggestion".Translate());
						foreach (XmlNode node in correctionAttempt)
						{
							lastSelectPieceMeal[0] = node.Name; //Substitute wildcard match
							xpathTree[xpathTree.Length - 1] = string.Join("", lastSelectPieceMeal);
							correctedXPath = string.Join("", xpathTree);
							stringBuilder.AppendLine(correctedXPath.Colorize(ColorLibrary.Grey));
						}
					}
				}
			}
			catch (Exception ex)
			{
				//stringBuilder.AppendLine($"Exception {ex}");
			}
		}

		private void AttemptDefNameToAttributeAutoCorrect(StringBuilder stringBuilder, params (string name, string proposal)[] attempts)
		{
			try
			{
				if (!attempts.NullOrEmpty())
				{
					HashSet<string> suggestions = new HashSet<string>(); //Avoid duplicate suggestions
					string xpathNoWhitespace = string.Concat(xpath.Where(c => !char.IsWhiteSpace(c)));
					foreach ((string name, string proposal) in attempts)
					{
						if (xpathNoWhitespace.Contains($"[{name}="))
						{
							string correctedXPath = xpath.Replace(name, proposal);
							XmlNodeList correctionAttempt = document.SelectNodes(correctedXPath);
							if (correctionAttempt.Count > 0)
							{
								foreach (XmlNode node in correctionAttempt)
								{
									suggestions.Add(correctedXPath.Colorize(ColorLibrary.Grey));
								}
							}
						}
					}

					if (suggestions.Count > 0)
					{
						stringBuilder.AppendLine();
						stringBuilder.AppendLine("CloseSearchSuggestion".Translate());
						foreach (string suggestion in suggestions)
						{
							stringBuilder.AppendLine(suggestion);
						}
					}
				}
			}
			catch (Exception ex)
			{
				//stringBuilder.AppendLine($"Exception {ex}");
			}
		}

		private void AttemptDefNameCloseMatch(StringBuilder stringBuilder)
		{
			try
			{
				HashSet<string> suggestions = new HashSet<string>(); //Avoid duplicate suggestions
				string regex = @"[a-zA-Z0-9_]+\=\""[a-zA-Z0-9_]+\""";
				string xpathNoWhitespace = string.Concat(xpath.Where(c => !char.IsWhiteSpace(c)));
				string[] xpathNoPredicates = Regex.Split(xpathNoWhitespace, $@"\[{regex}\]");
				if (xpathNoPredicates.Length == 2)
				{
					Match match = Regex.Match(xpathNoWhitespace, regex);
					string xpathCheckForCloseMatches = $"{xpathNoPredicates[0]}[contains({match.Value.Replace("=", ",")})]";
					XmlNodeList correctionAttempt = document.SelectNodes(xpathCheckForCloseMatches);
					if (correctionAttempt.Count > 0)
					{
						stringBuilder.AppendLine();
						stringBuilder.AppendLine("CloseSearchSuggestion".Translate());
						for (int i = 0; i < correctionAttempt.Count; i++)
						{
							string nodeName = match.Value.Split('=').FirstOrDefault();
							XmlNode correctNode = correctionAttempt[i][nodeName];
							if (correctNode != null)
							{
								string correctedXPath = $"{xpathNoPredicates[0]}[{Regex.Replace(match.Value, @$"\""[a-zA-Z0-9_]+\""", $"\"{correctNode.InnerText}\"")}]";
								suggestions.Add(correctedXPath.Colorize(ColorLibrary.Grey));
							}
						}

						foreach (string suggestion in suggestions)
						{
							stringBuilder.AppendLine(suggestion);
						}
					}
				}
			}
			catch (Exception ex)
			{
				//stringBuilder.AppendLine($"Exception {ex}");
			}
		}

		private void AttemptBackTraversalCorrect(StringBuilder stringBuilder)
		{
			try
			{
				MatchCollection matches = Regex.Matches(xpath, @"(\/\.\.)|(\/parent::node\(\))");
				if (matches.Count > 0)
				{
					string[] xpathTree = Regex.Split(xpath, @"(\/{1,2})");
					int indexLastParentSelector = Mathf.Max(Array.LastIndexOf(xpathTree, ".."), Array.LastIndexOf(xpathTree, "parent::node()"));
					string xpathCorrected = "";
					int length = xpathTree.Length - matches.Count * 2;
					int replacementIndex = indexLastParentSelector + 1 - matches.Count * 4;
					for (int i = 0; i < xpathTree.Length; i++)
					{
						string curSelection = xpathTree[i];
						if (Regex.IsMatch(curSelection, @"(\/{1,2})") && Regex.IsMatch(xpathTree[i + 1], @"(\.\.)|(\/parent::node\(\))"))
						{
							i += 1; //skip parent selectors in corrected xpath
							if (i == indexLastParentSelector)
							{
								xpathCorrected += "]"; //close predicate suggestion
							}
						}
						else
						{
							if (i == replacementIndex)
							{
								curSelection = "[";
							}
							xpathCorrected += curSelection;
						}
					}
					string correctedXPath = string.Join("", xpathTree);
					XmlNodeList correctionAttempt = document.SelectNodes(correctedXPath);
					if (correctionAttempt.Count > 0)
					{
						stringBuilder.AppendLine();
						stringBuilder.AppendLine("UpTreeTraversalSuggestion".Translate().Colorize(ColorLibrary.RedReadable));
						stringBuilder.AppendLine(xpathCorrected.Colorize(ColorLibrary.Grey));
					}
				}
			}
			catch (Exception ex)
			{
				//stringBuilder.AppendLine($"Exception {ex}");
			}
		}

		private double ProfileSelect(int sample)
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
					_ = nodeList.Count;
					stopwatch.Stop();
					results.Add(stopwatch.ElapsedTicks);
					ms.Add(stopwatch.ElapsedMilliseconds);
					//Only check for exit point if given enough sample points to determine if operation is too slow
					if (ms.Sum() > TotalAllowedProfilingTime * 1000)
					{
						summary.AppendLine("ProfileExitOperationTooLong".Translate(i));
						break;
					}
					LongEventHandler.SetCurrentEventText($"{"XPathProfiling".Translate()} {i}/{sample}");
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

		public static void BuildXmlSummary(XmlNodeList nodeList, StringBuilder summary, int maxNodeCount = 50)
		{
			List<XmlNode> listNodeList = new List<XmlNode>();
			foreach (XmlNode node in nodeList)
			{
				listNodeList.Add(node);
			}
			BuildXmlSummary(listNodeList, summary, maxNodeCount);
		}

		public static void BuildXmlSummary(List<XmlNode> nodeList, StringBuilder summary, int maxNodeCount = 50)
		{
			int i = 0;
			bool truncate = nodeList.Count > 1 || (nodeList.Count == 1 && (nodeList[0].ChildNodes.Count > maxNodeCount || nodeList[0].Name == "#document"));
			foreach (XmlNode node in nodeList)
			{
				depth = 0;
				tabs = 0;
				nodesPerMatch = 0;
				AppendXmlRecursive(node, truncate, summary);
				if (i >= XmlPatchMod.settings.resultsLimit)
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
				inputRect.height = Text.CalcHeight((string)value, inputRect.width);
				string newValue = TextArea(inputRect, (string)value, true, null, TextAnchor.MiddleLeft);
				if (newValue != (string)value)
				{
					value = newValue;
					onValueChange?.Invoke(value);
				}
				inputRect.y += inputRect.height;
				inputRect.height = 24;
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
						XmlElement element = doc.CreateElement("value");

						if (node.NullOrEmpty())
						{
							element.InnerText = text;
						}
						else
						{
							XmlElement innerElement = doc.CreateElement(node);
							if (!attributes.NullOrEmpty())
							{
								foreach ((string name, string value) in attributes)
								{
									element.SetAttribute(name, value);
								}
							}
							innerElement.InnerText = text;
							element.AppendChild(innerElement);
						}
						container.node = element;
						onValueChange?.Invoke(value);
					}));
				}

				inputRect.y += buttonRect.height + 2;

				float summaryHeight = 0;
				float summaryY = inputRect.y;
				if (container.node.IsEmpty() || container.node.FirstChild.IsEmpty())
				{
					summaryHeight += inputRect.height;
					TextArea(inputRect, XmlText.OpenSelfClosingBracket(label));
				}
				else
				{
					summaryHeight += inputRect.height;
					string summary = containerSummaries[field].ToString().TrimEndNewlines();
					inputRect.height = Text.CalcHeight(summary, inputRect.width);
					TextArea(inputRect, summary);
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
