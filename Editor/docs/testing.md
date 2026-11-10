# Testing

CardForge uses Unity Test Framework (NUnit) editor tests. Tests are split into three assemblies by dependency.

Related: [contributing.md](contributing.md), [architecture.md](architecture.md).

## Test assemblies

| Assembly | Location | References | Focus |
| --- | --- | --- | --- |
| `CardForge.Tests` | `Assets/CardForge/Tests/Editor` | Runtime, Editor | Import and rule matching. |
| `CardForge.DesignLogic.Tests` | `Assets/CardForge/Tests/DesignLogic/Editor` | Runtime, DesignLogic | Geometry. |
| `CardForge.CreatorTools.Tests` | `Assets/CardForge/Tests/CreatorTools/Editor` | Runtime, Editor, DesignLogic, ImageProcessing, Ollama, GoogleFonts, IconSets | Creator-tool logic. |

All three:

- Target the Editor platform only.
- Use `optionalUnityReferences: ["TestAssemblies"]`.
- Are constrained by `UNITY_INCLUDE_TESTS`.
- Set `autoReferenced: false`.

## Running tests

### Test Runner window

1. Open **Window > General > Test Runner**.
2. Select the **EditMode** tab.
3. Click **Run All**, or run a single assembly/class/test.

### Command line

```
Unity -batchmode -projectPath <path> -runTests -testPlatform EditMode \
  -testResults results.xml -quit
```

Use the same Unity version as `ProjectSettings/ProjectVersion.txt` (currently `7000.0.0a16`).

## Import tests

`Assets/CardForge/Tests/Editor/ImportTests.cs`:

| Test | Verifies |
| --- | --- |
| `CsvHandlesQuotedMultilineAndBom` | BOM stripping, quoted commas, embedded newlines and doubled quotes. |
| `DetectsDelimiters` | Semicolon and tab detection. |
| `RejectsMalformedCsv` | Unterminated quote and a row wider than the header. |
| `JsonReadsArbitraryHeadersAndUnicode` | Union of keys across records, `\u` escapes, booleans. |
| `JsonRejectsMalformedInput` | Duplicate keys and trailing commas. |
| `SheetsPreservesWorksheetGid` | Google Sheets export URL keeps the `gid`. |
| `DocsExtractsFirstTable` | First HTML table becomes the import table. |
| `RejectsGoogleLookalikeHost` | `docs.google.com.evil.test` is rejected. |
| `ExcelReadsMultipleWorksheets` | `.xls` and `.xlsx` fixtures expose both sheets. |
| `RequirementsUseNumericComparison` | Numeric requirement comparisons. |

Fixtures live in `Assets/CardForge/Tests/Fixtures/`:

- `TestMultiSheet.xls`
- `TestMultiSheet.xlsx`

## Design logic tests

`Assets/CardForge/Tests/DesignLogic/Editor/FrameGeometryTests.cs`:

| Test | Verifies |
| --- | --- |
| `NormalizedRegionsStayWithinImage` | Normalized rectangles convert and clamp inside image bounds. |
| `NonFiniteAndDegenerateRegionsAreRejected` | NaN/infinity and zero-size boxes are rejected. |
| `UnevenAtlasCellsCoverEntireImage` | Atlas cell math covers the image when divisions are uneven. |
| `FileNameIsSanitized` | The slug helper produces safe file names. |

## Creator tool tests

`Assets/CardForge/Tests/CreatorTools/Editor/CreatorToolsTests.cs`:

| Test | Verifies |
| --- | --- |
| `OllamaJsonResponseProducesClampedRegions` | Vision JSON parses into clamped zone proposals. |
| `OllamaRejectsEmptyRegions` | Empty/unusable responses raise an error. |
| `FontDownloadsAreRestrictedToTrustedHosts` | The font host allow-list rejects untrusted URLs. |
| `FrameEdgeRefinerAlignsProposedBoxToHighContrastBorder` | The edge refiner moves a box toward a strong border. |
| `CropProducesCorrectPixels` | Cropping returns the expected pixels. |

## What is not covered

There are no automated tests for:

- The UI Toolkit shell, grids, dialogs or designer.
- The store's save/load/delete/export paths.
- Bundle building and runtime bundle loading.
- The rule engine's response windows.
- Asset import or Unity's texture pipeline.

If you change these areas, test manually with the seeded `CORE` block and consider adding a test assembly.

## Adding a test

1. Choose the assembly whose references you need. Do not add editor-only references to the runtime test assembly.
2. Add a `[Test]` or `[TestCase]` method.
3. Keep tests deterministic: no network, no reliance on the current time, no mutation of `Assets/CardForgeData`.
4. For file fixtures, place them under `Assets/CardForge/Tests/Fixtures/` and commit their `.meta` files.
5. For pure logic, prefer extracting it into `CardForge.DesignLogic` or another runtime-safe assembly so it is easy to test.

Example:

```csharp
using NUnit.Framework;

namespace CardForge.Tests
{
    public sealed class MyTests
    {
        [TestCase("a,b\n1,2", "2")]
        public void ParsesValue(string text, string expected)
        {
            Assert.AreEqual(expected, text.Split('\n')[1].Split(',')[1]);
        }
    }
}
```

## Manual test checklist

Before shipping a change:

- [ ] Open the studio and switch every library screen.
- [ ] Create a layout, add each zone kind, drag and resize.
- [ ] Add a layer and a variant; toggle Preview variants.
- [ ] Create a card, fill every schema field, assign art and a back.
- [ ] Clone a block with cards; confirm reprint provenance.
- [ ] Validate the block and export a bundle.
- [ ] Load the bundle with `CardForgeRuntimeReader` in a scene.
- [ ] Import a CSV with a new and an existing id; test all three policies.
- [ ] Export JSON with linked assets and re-import into a clean project.
- [ ] Run Frame Composer local detection and save a layout.
- [ ] Import an icon atlas and a font.

## See also

- [troubleshooting.md](troubleshooting.md) for test runner issues.
- [contributing.md](contributing.md) for conventions.