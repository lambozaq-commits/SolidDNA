# Cabin Tools property and dimension task pane — implementation handoff

Date: 1 October 2026  
Source baseline: user-supplied `Add in.zip`  
Target: SOLIDWORKS 2026, .NET Framework 4.8, CADBooster SolidDNA 4.0.0

## Verification level

- Static-reviewed: complete for the new property-pane code paths.
- Build-confirmed: Debug and Release, 0 errors and 0 warnings.
- Logic-confirmed: built-in profile, mappings, ambiguity, XML round-trip, deep clone, and invalid-profile rejection.
- Registration-confirmed: pending.
- Application-smoke-confirmed: pending.
- Feature-confirmed: pending in SOLIDWORKS.
- Regression-confirmed: pending in SOLIDWORKS.

Current accurate status: **Build-verified, application test pending.**

The build was deliberately not deployed while SOLIDWORKS was running. No open
SOLIDWORKS document was modified or saved.

## Implemented behavior

- One persistent task pane follows the active part, assembly, drawing, unsupported
  document, or no-document state.
- Part, assembly, and drawing layouts use coherent collapsible groups and the same
  `Label | Value | Scope | Clear` row pattern.
- Property scopes support Document, Active configuration, Selected configurations,
  and All configurations where applicable.
- Multi-configuration values display `<varies>` and remain unchanged until the user
  enters a replacement.
- Configuration Description takes precedence over document Description; blank
  configuration Description inherits the document value and is marked inherited.
- Blank means no change by default. Clear is an explicit confirmed action.
- Apply writes only changed fields, rebuilds once, and never saves.
- Property and dimension writes participate in one coordinated operation. A partial
  write or rebuild failure triggers best-effort restoration with accurate reporting.
- Drawing `Title2`, `Title 3`, and `DrwNumber` use automatic behavior. Drawing-number
  no-match and ambiguity never invent a result. Manual override is disabled by default.
- Part dimensions support Bottom elevation, Height, Width, and Depth in mm with
  Active, Selected, and All configuration scopes.
- Dimension discovery verifies exact feature names, reference-plane feature types,
  distance/offset definition, configured source-plane COM identity, target
  suppression state, and controlling display dimension availability.
- Parts containing a design table show a warning immediately before dimension writes.
- Read-only, view-only, PDM checked-in, and filesystem read-only states display data
  but block Apply and Clear. There is no automatic checkout or save.
- Settings provide layout/group/field editing, field behavior and rules, dropdown
  lists, drawing mappings, dimension mappings, editable property catalog, import,
  export, reset, backup, and schema migration.
- Existing command IDs, COM GUIDs, toolbar commands, and the `Advanced Save` name are
  unchanged.

## Architecture placement

```text
SolidDnaPlugin
  -> CabinToolsTaskpaneHost (WinForms rendering and user interaction)
       -> PropertyPaneApplyCoordinator
            -> PropertyPanePropertyService
            -> PropertyPaneDimensionService
       -> DrawingNumberResolver
       -> PropertyPaneSettingsService
            -> versioned Cabin Tools XML profile
```

Raw SOLIDWORKS property and dimension calls remain in the focused services rather
than the plug-in bootstrap. The coordinator owns cross-service rollback and rebuild.
No assembly-component traversal was added.

## Profile and persistence

Profile location:

`%APPDATA%\CabinTools\Settings\PropertyPaneProfile.xml`

The existing XML settings pattern was retained instead of introducing another
serializer. Schema version is now 2. Schema 1 profiles are migrated in place after
creating a timestamped backup. Invalid or newer profiles are preserved for diagnosis,
then safe built-in defaults are loaded. Save replacement keeps a
`PropertyPaneProfile.xml.last-known-good` recovery file.

Built-in defaults are compiled into Cabin Tools. Property Tab Builder files and
`Cabin Properties.xlsx` are not required at runtime. The pre-existing shared-workbook
option remains optional and retains a local-profile fallback.

## Material SOLIDWORKS API basis

The signatures and enum values below were checked against the installed SOLIDWORKS
2026 interop assemblies (`34.1.1.11`) and official API Help where available.

| Purpose | API basis | Units / result handling |
|---|---|---|
| Read properties | `ICustomPropertyManager.Get6` and `GetType2` | Uses uncached resolution; checks `swCustomInfoGetResult_e` semantics. |
| Add/update properties | `ICustomPropertyManager.Add3` with `swCustomPropertyReplaceValue` | Preserves the existing property type; success is `swCustomInfoAddResult_AddedOrChanged`. |
| Explicit clear | `ICustomPropertyManager.Delete2` | Success and not-present are distinguished with `swCustomInfoDeleteResult_e`. |
| Property scope | `IModelDocExtension.CustomPropertyManager[configurationName]` | Empty string means document-level; exact configuration name means configuration-specific. |
| Configurations | `IModelDoc2.GetConfigurationNames` and `IConfigurationManager.ActiveConfiguration` | All selected names are validated before writing. No configuration activation is required for property writes. |
| Plane discovery | `IModelDoc2.FirstFeature`, `IFeature.GetNextFeature`, `GetTypeName2`, `GetDefinition` | Exact case-sensitive configured feature-name match; feature type must be `RefPlane`. |
| Plane definition | `IRefPlaneFeatureData.AccessSelections`, `Type2`, `Constraint`, `Reference`, `ReleaseSelectionAccess` | Confirms distance/offset constraint and configured source-plane COM identity. |
| Suppression | `IFeature.IsSuppressed2` with `swSpecifyConfiguration` | Every target configuration is checked before a dimension write. |
| Dimension read | `IDimension.GetSystemValue3` | SOLIDWORKS system units are metres; converted to mm only outside the API call. |
| Dimension write | `IDimension.SetSystemValue3` | mm is converted to metres; return code is decoded with `swSetValueReturnStatus_e`. |
| Rebuild | `IModelDoc2.ForceRebuild3(false)` | `false` return is treated as rebuild failure and initiates restoration. No save call is made. |
| Write access | `IModelDoc2.IsOpenedReadOnly`, `IsOpenedViewOnly`, and filesystem attributes | Never changes read-only state and never checks out from PDM. |
| Design table | `IModelDoc2.GetDesignTable` | Presence triggers a user warning before dimension writes. |

## Changed and added files

- `CabinToolsTaskpaneHost.cs` — compact coherent UI, groups, field rules,
  validation, drawing automation, design-table warning, coordinator integration.
- `PropertyPaneModels.cs` — schema 2 field metadata, control types, coherent defaults,
  exact drawing expressions, default scopes.
- `PropertyPaneServices.cs` — effective Description, validated scopes, drawing resolver,
  transactional property/dimension services, strict plane discovery, rollback,
  rebuild coordination, logging.
- `PropertyPaneSettingsService.cs` — migration, deep cloning, stronger validation,
  atomic replacement, recovery copy.
- `PropertyPaneDialogs.cs` — consistent settings UI, field editor, catalog editor,
  group rename/reorder, shared visual styling.
- `CabinCustomPropertyStore.cs` — view-only write protection.
- `SolidDNA.csproj` — safe build-time deployment bypass switch.
- `Properties/AssemblyInfo.cs` — restored assembly metadata omitted from the supplied ZIP.
- `tests/Run-PropertyPaneLogicTests.ps1` — repeatable non-SOLIDWORKS logic checks.
- `docs/CabinTools_UI_and_Tool_Logic_Guide_v3_11_0.txt` — updated final behavior guide.
- `docs/PROPERTY_PANE_IMPLEMENTATION_HANDOFF.md` — this handoff.

No existing command GUID, add-in GUID, task-pane GUID, command ID, or command name was
changed.

## Automated checks executed

Build commands used the `SkipCabinToolsDeploy=true` property so the installed DLL was
not overwritten while SOLIDWORKS was open.

- Debug rebuild: passed, 0 errors, 0 warnings.
- Release rebuild: passed, 0 errors, 0 warnings.
- Known drawing mapping: passed.
- Drawing mapping no-match: passed.
- Conflicting mapping ambiguity: passed.
- Profile schema/layout/dimension defaults: passed.
- Profile XML serialization round-trip: passed.
- Settings deep clone: passed.
- Duplicate stable field-ID rejection: passed.

Run the logic checks again with:

```powershell
.\tests\Run-PropertyPaneLogicTests.ps1
```

## Exact manual SOLIDWORKS test procedure

### Preparation and deployment

1. Close SOLIDWORKS before deployment. This avoids replacing a loaded COM DLL.
2. Back up `C:\ProgramData\CabinTools\SolidDNA.dll` and the current
   `%APPDATA%\CabinTools\Settings\PropertyPaneProfile.xml`.
3. Deploy the Release output using the supplied deployment script.
4. Start SOLIDWORKS 2026 and confirm Cabin Tools loads once with one task pane.
5. Confirm the existing CommandManager commands remain present and that the command
   name is still exactly `Advanced Save`.

### Document switching

1. With no document open, confirm `No active document`.
2. Open the reference-only main cabin assembly `019325`; confirm assembly fields,
   filename, configuration, and access state. Do not press Apply.
3. Switch to the reference-only window-box part `016880`; confirm part fields,
   Dimensions, and that a dimension edit would present a design-table warning.
   Cancel the warning and do not save this part.
4. Switch among documents/configurations and confirm the pane refreshes once without
   duplicate controls or losing pending edits silently.

### Writable assembly test (`Assem2` only)

1. Make `Assem2` active and confirm it is writable.
2. Record the current active configuration and save a separate backup copy manually.
3. Change one harmless test property at Document scope and press Apply.
4. Confirm the property changes, the assembly rebuilds, and the title bar indicates
   unsaved changes. Confirm no automatic save occurred.
5. Set the same property for Active configuration and verify the document-level value
   is unchanged.
6. Use Selected configurations... and All configurations; verify only intended
   configurations change and the original active configuration remains active.
7. Create differing values, select multiple configurations, and confirm `<varies>`.
   Leave it untouched and Apply another field; verify differing values remain.
8. Test explicit Clear in one scope and confirm other scopes remain unchanged.
9. Close without saving or restore the recorded test values.

### Part dimension test

1. Use a writable copy of the representative part, never production `016880`.
2. Confirm all eight configured plane names exist and that the four controlling planes
   have the specified source-plane relationships.
3. Record Bottom elevation, Height, Width, and Depth for every configuration.
4. Test Active, Selected..., and All scopes one at a time with small reversible changes.
5. Confirm mm values are correct, untouched configurations are unchanged, rebuild is
   successful, and no save occurs.
6. Verify zero/negative Height, Width, and Depth are blocked. Verify finite negative
   Bottom elevation is accepted only if the model itself accepts it.
7. Rename or suppress a controlling plane in a disposable copy. Confirm the specific
   dimension is disabled while property editing remains usable.
8. Restore values and discard or close the test copy without saving if appropriate.

### Drawing test

1. Open a writable representative `.SLDDRW` copy.
2. Enter a known valid cabin/definition/layout combination and confirm the unique
   DrwNumber preview and write.
3. Test no match and conflicting mappings; confirm no number is invented.
4. Confirm `Title2` and `Title 3` expressions and evaluated previews.
5. Enable manual override in Settings, verify it is visibly editable, then disable it.
6. Test Revision, Designer, Checked By, and Checked Date. Confirm no automatic save.

### Regression

Run Advanced Save, Property Checker, Configuration Property Editor, Cut-List Property
Editor, PDF export, neutral export, drawing tools, assembly tools, and task-pane
disable/enable. Confirm existing command IDs, names, toolbar placement, and S-key
customizations remain stable.

## Known limitations and pending validation

- The strict plane-reference identity check is deliberately conservative and must be
  feature-confirmed on the representative part before production deployment.
- The supplied materials did not include `016989.SLDPRT` or a representative drawing,
  so final part/drawing feature confirmation remains pending.
- The current environment could compile against the installed SOLIDWORKS 2026 interop,
  but reliable live-document enumeration was not available. No claim of application
  testing is made.
- Drawing fields use document-level storage in this implementation. The profile model
  remains scope-aware, but drawing configuration-specific storage is not exposed
  because the established Cabin Tools adapter supports configuration properties only
  for parts and assemblies.
- Shared Excel remains an optional compatibility feature. Built-in/local profile data
  is always available and is the runtime fallback.

## Rollback

1. Close SOLIDWORKS.
2. Restore the previous `C:\ProgramData\CabinTools\SolidDNA.dll` (and matching PDB and
   resources, if deployed together) from the deployment backup.
3. Restore the prior profile from `%APPDATA%\CabinTools\Settings\Backups` or the
   `.last-known-good` file if required.
4. Re-register only if normal add-in registration was changed during deployment.
5. Start SOLIDWORKS and verify the previous Cabin Tools build and command set.

