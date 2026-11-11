using System;
using System.IO;
using System.Linq;
using CardForge.Editor.Importing;
using NUnit.Framework;
using UnityEditor;

namespace CardForge.Tests
{
    public sealed class ImportTests
    {
        [Test] public void CsvHandlesQuotedMultilineAndBom()
        {
            var table=TabularReader.ReadDelimited("\uFEFFname,cost,rules\r\n\"Ember, Drake\",4,\"Line 1\nLine \"\"2\"\"\"\r\n");
            Assert.AreEqual("name",table.Headers[0]);Assert.AreEqual(1,table.Rows.Count);Assert.AreEqual("Ember, Drake",table.Rows[0][0]);Assert.AreEqual("Line 1\nLine \"2\"",table.Rows[0][2]);
        }
        [TestCase("name;cost\na;2")][TestCase("name\tcost\na\t2")]
        public void DetectsDelimiters(string text)=>Assert.AreEqual("2",TabularReader.ReadDelimited(text).Rows[0][1]);
        [TestCase("name,cost\n\"oops")][TestCase("name\na,b")]
        public void RejectsMalformedCsv(string text)=>Assert.Throws<FormatException>(()=>TabularReader.ReadDelimited(text));
        [Test] public void JsonReadsArbitraryHeadersAndUnicode()
        {
            var table=TabularReader.ReadJson("[{\"name\":\"é\\u2603\",\"cost\":2,\"enabled\":true}]");Assert.AreEqual("é☃",table.Rows[0][0]);Assert.AreEqual("true",table.Rows[0][2]);
        }
        [TestCase("{\"a\":1,\"a\":2}")][TestCase("[1,]")]
        public void JsonRejectsMalformedInput(string text)=>Assert.Throws<FormatException>(()=>JsonReader.Parse(text));
        [Test] public void SheetsPreservesWorksheetGid()
        {
            var url=TabularReader.GoogleExportUrl("https://docs.google.com/spreadsheets/d/test_ID/edit#gid=123",out var document);Assert.IsFalse(document);StringAssert.EndsWith("gid=123",url);
        }
        [Test] public void DocsExtractsFirstTable()
        {
            var table=TabularReader.ReadGoogleDocumentHtml("<table><tr><td>Name</td></tr><tr><td>One &amp; Two</td></tr></table>");Assert.AreEqual("One & Two",table.Rows[0][0]);
        }
        [Test] public void RejectsGoogleLookalikeHost()=>Assert.Throws<FormatException>(()=>TabularReader.GoogleExportUrl("https://docs.google.com.evil.test/document/d/test",out _));
        [TestCase("xls")][TestCase("xlsx")]
        public void ExcelReadsMultipleWorksheets(string extension)
        {
            string path=AssetDatabase.FindAssets("TestMultiSheet").Select(AssetDatabase.GUIDToAssetPath).First(p=>p.EndsWith("."+extension,StringComparison.OrdinalIgnoreCase));Assert.GreaterOrEqual(TabularReader.ReadFile(path).Count,2);
        }
        [Test] public void RequirementsUseNumericComparison()
        {
            var field=new FieldDefinition{key="cost",kind=FieldKind.Number};var schema=new SchemaDocument();schema.fields.Add(field);var card=new CardDocument();CardForgeData.SetValue(card,field.id,"5");
            Assert.IsTrue(CardForgeRules.Matches(new[]{new RequirementDefinition{fieldId=field.id,comparison="Greater than",expectedValue="4"}},card,schema));
        }
    }
}
