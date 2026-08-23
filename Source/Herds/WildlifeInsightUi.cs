using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using InsightCanvas;
using UnityEngine;
using Verse;

namespace Herds
{
    internal static class WildlifeUiText
    {
        public static string Get(string key) => key.Translate();

        public static string Field(string name)
        {
            string key = "Wildlife.UI.Settings.Field." + name;
            string translated = key.Translate();
            return translated == key ? SplitWords(name) : translated;
        }

        public static string EnumValue<T>(T value) => Get("Wildlife.UI.Profiles.Enum." + value);

        public static string FormatNumber(object value)
        {
            if (value is float)
                return ((float)value).ToString("0.##", CultureInfo.InvariantCulture);
            if (value is double)
                return ((double)value).ToString("0.##", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static string SplitWords(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return string.Concat(value.Select((character, index) =>
                index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));
        }
    }

    internal static class WildlifeUiTheme
    {
        public static void Configure(InsightUiDocument document, bool highContrast, bool reducedMotion)
        {
            InsightTheme theme = InsightTheme.Default.Clone();
            theme.Id = "wildlife-scoped";
            theme.Selected = new InsightColor(0.24f, 0.62f, 0.70f);
            theme.Focus = new InsightColor(0.90f, 0.70f, 0.30f);
            theme.Spacing = 8f;
            document.Theme = theme;
            document.Density = InsightUiDensity.Normal;
            document.HighContrast = highContrast;
            document.ReducedMotion = reducedMotion;
            document.TrackDuplicateIds = true;
        }
    }

    internal sealed class WildlifeSettingsPresentation
    {
        private sealed class Category
        {
            public readonly string Id;
            public readonly string LabelKey;
            public readonly string DescriptionKey;
            public readonly string[] Fields;

            public Category(string id, string labelKey, string descriptionKey, params string[] fields)
            {
                Id = id;
                LabelKey = labelKey;
                DescriptionKey = descriptionKey;
                Fields = fields ?? new string[0];
            }
        }

        private static readonly Category[] Categories =
        {
            new Category("overview", "Wildlife.UI.Settings.Category.Overview", "Wildlife.UI.Settings.Category.Overview.Description",
                "enablePreyAndHerds", "enableHuntingChanges", "enableResearchProgression", "gateHuntingByResearch",
                "gateKnowledgeByResearch", "gateStewardshipByResearch", "gateIndustrialEcologyByResearch",
                "enablePlayerOnboarding", "enableOutcomeHistory", "enableUnlockLetters", "enableFieldJournal",
                "enableWildlifeManagementGoals", "enableStewardProjects", "enableHuntRewards"),
            new Category("herds-prey", "Wildlife.UI.Settings.Category.HerdsPrey", "Wildlife.UI.Settings.Category.HerdsPrey.Description",
                "updateIntervalTicks", "unpennedJoinDistance", "coordinateWildHerds", "enableDefensiveBehavior",
                "defenseScanIntervalTicks", "protectiveBodySizeThreshold", "flightDistance", "enableHiding",
                "allowNaturalBurrows", "maximumInferredHidingBodySize", "defaultHideSuccessChance", "hideEntryTicks",
                "failedHideRetryTicks", "refugeRefreshIntervalTicks", "minimumHideTicks", "maximumHideTicks"),
            new Category("predators", "Wildlife.UI.Settings.Category.Predators", "Wildlife.UI.Settings.Category.Predators.Description",
                "enablePredatorEscapeChance", "basePredatorEscapeChance", "predatorEscapeCheckIntervalTicks",
                "enablePredatorDeterrents", "enableUncertainPredatorWarnings", "enableRanchGuardians",
                "guardiansAttackPredators", "enableDomesticPredatorRoles", "enableScavenging", "preyAvoidColonists"),
            new Category("fieldcraft", "Wildlife.UI.Settings.Category.Fieldcraft", "Wildlife.UI.Settings.Category.Fieldcraft.Description",
                "enableObservationPosts", "enableWildlifeBait", "enableWildlifeReserves", "enableWildlifeAlerts",
                "enableTrackingSigns", "enableTrailReading", "enableWindHud", "enableScentMasking", "enableAnimalCalls",
                "enableMannedBlinds", "enableRanchGuardians", "guardiansAttackPredators", "enableHuntingExpeditions",
                "enableUncertainPredatorWarnings", "enableWildlifeSteward", "enableGuardianPatrolAreas",
                "minimumFieldcraftSkill", "enableWeaponAwareTactics", "enableWoundedTrackingAndRetreat", "enableHuntTracking",
                "enableHuntedAdrenaline", "enableHuntEndurance", "enableAdaptivePreyResponses", "enableFieldcraftEquipment",
                "enableCameraTraps", "enableTelemetry", "enableDiseaseMonitoring", "enableAppliedEcology"),
            new Category("knowledge", "Wildlife.UI.Settings.Category.Knowledge", "Wildlife.UI.Settings.Category.Knowledge.Description",
                "enableWildlifeKnowledge", "requireObservationForDetails", "enableSpeciesKnowledgeProgression",
                "enableWildlifeLearning", "enablePlayerSignalImitation", "enablePredatorSignalLearning"),
            new Category("ecology", "Wildlife.UI.Settings.Category.Ecology", "Wildlife.UI.Settings.Category.Ecology.Description",
                "enableEcologicalConsequences", "enableWildlifeLandscaping", "enableLandscapeEffects", "enableLandscapeCrossroads",
                "enableHabitatEcology", "enableRegionalPopulations", "enableRegionalMigration", "enablePersistentRoamingAnimals",
                "enableRoamingExpeditionEncounters", "enableReturnSigns", "enableVisibleMigrationWaves", "enableTerritoryHistory",
                "enableConservationActions", "enablePopulationConsequences", "enableWildlifeEvents", "enableSeasonalEcologyEvents"),
            new Category("animal-lives", "Wildlife.UI.Settings.Category.AnimalLives", "Wildlife.UI.Settings.Category.AnimalLives.Description",
                "enableAnimalMemory", "enableAnimalSocialMemory", "frightenedMemoryLifetimeTicks", "packMemberKilledMemoryLifetimeTicks",
                "enableJuvenileLearning", "enablePersistentFamilyLines", "enableAnimalRelationships", "enableAnimalPersonalities",
                "enablePersonalityInheritance", "enableWildlifeLifeIncidents", "enableDomesticRoleProgression"),
            new Category("culture-folklore", "Wildlife.UI.Settings.Category.CultureFolklore", "Wildlife.UI.Settings.Category.CultureFolklore.Description",
                "enableWildlifeSignalCulture", "showIdentifiedSignalText", "enableAnimalTraditions", "enableColonyWildlifeLandmark",
                "enableWildlifeFolklore", "enableWildlifeIdeology", "enableFolkloreRetelling", "enableCulturalAnimals",
                "enableWildlifeCeremonies", "enableFolkloreDisplays", "enableLegendSpread", "enableLegendQuests",
                "enablePhysicalWildlifeStories", "enableLegendaryPresentation", "enableWildlifeIdeologyRoles"),
            new Category("expeditions", "Wildlife.UI.Settings.Category.Expeditions", "Wildlife.UI.Settings.Category.Expeditions.Description",
                "enableHuntingExpeditions", "enableOffMapHuntingExpeditions", "enableExtendedHuntingExpeditions",
                "enableExpeditionIncidents", "enableExpeditionBiomeEvents", "enableInteractiveExpeditionEncounters",
                "allowExpeditionDeaths", "enableVisibleMigrationWaves", "enableRoamingExpeditionEncounters"),
            new Category("advanced", "Wildlife.UI.Settings.Category.Advanced", "Wildlife.UI.Settings.Category.Advanced.Description",
                "enableAdvancedScavenging", "enableWildlifeManagementGoals", "enableOutcomeHistory", "hiddenPreySafeDistance",
                "enableNotableAnimals", "enableHuntingRegulations", "enableDynamicWildlifeOpportunities")
        };

        private readonly HerdsSettings settings;
        private readonly InsightUiDocument document;
        private readonly InsightUiHost host;
        private string search = string.Empty;

        public WildlifeSettingsPresentation(HerdsSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            document = new InsightUiDocument("wildlife-settings", InsightUi.Empty("settings-loading"));
            WildlifeUiTheme.Configure(document, false, false);
            document.Root = BuildRoot();
            host = new InsightUiHost(document);
        }

        public InsightUiDocument Document => document;

        public void Draw(Rect rect)
        {
            host.Draw(rect, Time.deltaTime);
        }

        public void PostClose()
        {
            host.PostClose();
        }

        private InsightUiElement BuildRoot()
        {
            InsightUiSearchField searchField = InsightUi.SearchField("settings-search", search,
                WildlifeUiText.Get("Wildlife.UI.Settings.SearchPlaceholder"), OnSearchChanged)
                .Bind(() => search, value => search = value ?? string.Empty);

            InsightUiNavigation navigation = InsightUi.Navigation("settings-categories", 720f);
            for (int i = 0; i < Categories.Length; i++)
            {
                Category category = Categories[i];
                navigation.Add(category.Id, WildlifeUiText.Get(category.LabelKey), BuildCategory(category));
            }

            List<InsightUiElement> headerItems = new List<InsightUiElement>
            {
                InsightUi.Breadcrumbs("settings-breadcrumbs", WildlifeUiText.Get("Wildlife.UI.Settings.Breadcrumb")),
                InsightUi.Label("settings-title", WildlifeUiText.Get("Wildlife.UI.Settings.Title"), InsightUiTextStyle.Title),
                InsightUi.Label("settings-subtitle", WildlifeUiText.Get("Wildlife.UI.Settings.Subtitle"), InsightUiTextStyle.Caption),
                searchField
            };
            if (!string.IsNullOrWhiteSpace(search) && !HasMatchingField())
                headerItems.Add(InsightUi.Callout("settings-global-no-results", InsightUiCalloutSeverity.Info,
                    WildlifeUiText.Get("Wildlife.UI.Settings.NoResults"),
                    WildlifeUiText.Get("Wildlife.UI.Settings.NoResults.Help")));
            InsightUiElement header = InsightUi.Column("settings-header", headerItems.ToArray()).SetGap(5f);
            return InsightUi.Column("settings-root", header, navigation).SetGap(10f).SetPadding(8f);
        }

        private bool HasMatchingField()
        {
            FieldInfo[] fields = typeof(HerdsSettings).GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].Name != "speciesOverrides" && Matches(fields[i])) return true;
            }
            return false;
        }

        private InsightUiElement BuildCategory(Category category)
        {
            string description = WildlifeUiText.Get(category.DescriptionKey);
            List<InsightUiElement> elements = new List<InsightUiElement>
            {
                InsightUi.SectionHeader(category.Id + ".header", WildlifeUiText.Get(category.LabelKey), description, divider: true),
                InsightUi.Callout(category.Id + ".info", InsightUiCalloutSeverity.Info,
                    WildlifeUiText.Get("Wildlife.UI.Settings.Compatibility.Title"), description)
            };

            HashSet<string> known = new HashSet<string>(category.Fields, StringComparer.Ordinal);
            for (int i = 0; i < category.Fields.Length; i++)
            {
                FieldInfo field = typeof(HerdsSettings).GetField(category.Fields[i], BindingFlags.Instance | BindingFlags.Public);
                if (field == null || field.Name == "speciesOverrides" || !Matches(field)) continue;
                elements.Add(BuildField(field, category.Id));
            }

            if (category.Id == "overview")
            {
                elements.Add(InsightUi.Button("overview-profiles", WildlifeUiText.Get("Wildlife.UI.Settings.OpenProfiles"), OpenProfiles));
                elements.Add(InsightUi.Label("overview-profile-help", WildlifeUiText.Get("Wildlife.UI.Settings.OpenProfiles.Help"), InsightUiTextStyle.Caption));
            }

            FieldInfo[] allFields = typeof(HerdsSettings).GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < allFields.Length; i++)
            {
                FieldInfo field = allFields[i];
                if (field.Name == "speciesOverrides" || known.Contains(field.Name) || category.Id != "advanced" || !Matches(field)) continue;
                elements.Add(BuildField(field, category.Id));
            }

            if (elements.Count == 2)
                elements.Add(InsightUi.Label(category.Id + ".empty", WildlifeUiText.Get("Wildlife.UI.Settings.NoMatches"), InsightUiTextStyle.Caption));
            return InsightUi.Scroll(category.Id + ".scroll", InsightUi.Column(category.Id + ".content", elements.ToArray()).SetGap(8f));
        }

        private bool Matches(FieldInfo field)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            string text = (WildlifeUiText.Field(field.Name) + " " + WildlifeUiText.SplitWords(field.Name)).ToLowerInvariant();
            return text.Contains(search.Trim().ToLowerInvariant());
        }

        private InsightUiElement BuildField(FieldInfo fieldInfo, string categoryId)
        {
            FieldInfo captured = fieldInfo;
            string id = "settings." + categoryId + "." + captured.Name;
            string label = WildlifeUiText.Field(captured.Name);
            if (captured.FieldType == typeof(bool))
            {
                bool enabled = IsFieldEnabled(captured.Name);
                InsightUiToggle toggle = InsightUi.Toggle(id, label, (bool)captured.GetValue(settings))
                    .Bind(() => (bool)captured.GetValue(settings), value =>
                    {
                        captured.SetValue(settings, value);
                        NotifyChanged();
                    });
                toggle.Enabled = enabled;
                InsightUiElement toggleElement = toggle.SetTooltip(GetFieldTooltip(captured.Name));
                if (!enabled)
                    return InsightUi.Column(id + ".disabled", toggleElement,
                        InsightUi.Label(id + ".disabled-help", DisabledExplanation(captured.Name), InsightUiTextStyle.Caption)).SetGap(2f);
                return toggleElement;
            }

            float minimum;
            float maximum;
            GetRange(captured.Name, captured.FieldType, out minimum, out maximum);
            InsightUiSlider slider = InsightUi.Slider(id + ".value", Convert.ToSingle(captured.GetValue(settings), CultureInfo.InvariantCulture), minimum, maximum)
                .Bind(() => Convert.ToSingle(captured.GetValue(settings), CultureInfo.InvariantCulture), value =>
                {
                    captured.SetValue(settings, captured.FieldType == typeof(int) ? (object)Mathf.RoundToInt(value) : value);
                    NotifyChanged();
                });
            slider.Enabled = IsFieldEnabled(captured.Name);
            InsightUiLabel valueLabel = InsightUi.Label(id + ".label", string.Empty, InsightUiTextStyle.Body)
                .SetTextProvider(() => label + ": " + WildlifeUiText.FormatNumber(captured.GetValue(settings)));
            InsightUiElement fieldElement = InsightUi.Column(id, valueLabel, slider).SetGap(3f)
                .SetTooltip(GetFieldTooltip(captured.Name));
            if (!IsFieldEnabled(captured.Name))
                fieldElement = InsightUi.Column(id + ".disabled", fieldElement,
                    InsightUi.Label(id + ".disabled-help", DisabledExplanation(captured.Name), InsightUiTextStyle.Caption)).SetGap(2f);
            return fieldElement;
        }

        private void OnSearchChanged(string value)
        {
            string next = value ?? string.Empty;
            if (search == next) return;
            search = next;
            document.Root = BuildRoot();
            document.Invalidate();
        }

        private void NotifyChanged()
        {
            HerdsMod.Instance?.RefreshSettingsRuntime();
            document.Root = BuildRoot();
            document.Invalidate();
        }

        private bool IsFieldEnabled(string fieldName)
        {
            string dependency = DependencyFor(fieldName);
            return dependency == null || (bool)typeof(HerdsSettings).GetField(dependency,
                BindingFlags.Instance | BindingFlags.Public).GetValue(settings);
        }

        private static string DependencyFor(string fieldName)
        {
            switch (fieldName)
            {
                case "enableLandscapeEffects":
                case "enableLandscapeCrossroads":
                    return "enableWildlifeLandscaping";
                case "showIdentifiedSignalText":
                case "enablePlayerSignalImitation":
                case "enablePredatorSignalLearning":
                    return "enableWildlifeSignalCulture";
                case "enableAnimalSocialMemory":
                case "frightenedMemoryLifetimeTicks":
                case "packMemberKilledMemoryLifetimeTicks":
                    return "enableAnimalMemory";
                case "enableHuntedAdrenaline":
                case "enableHuntEndurance":
                case "enableOffMapHuntingExpeditions":
                case "enableExtendedHuntingExpeditions":
                case "enableExpeditionIncidents":
                case "enableExpeditionBiomeEvents":
                case "enableInteractiveExpeditionEncounters":
                case "allowExpeditionDeaths":
                case "enableRoamingExpeditionEncounters":
                    return "enableHuntingExpeditions";
                default:
                    return null;
            }
        }

        private string DisabledExplanation(string fieldName)
        {
            string dependency = DependencyFor(fieldName);
            return WildlifeUiText.Get("Wildlife.UI.Settings.Disabled").Replace("{0}", WildlifeUiText.Field(dependency));
        }

        private string GetFieldTooltip(string fieldName)
        {
            string specific = "Wildlife.UI.Settings.Field." + fieldName + ".Help";
            string translated = specific.Translate();
            return translated == specific ? WildlifeUiText.Get("Wildlife.UI.Settings.Numeric.Help") : translated;
        }

        private static void OpenProfiles()
        {
            WildlifeSpeciesBehaviorPresentation.Open();
        }

        private void GetRange(string name, Type type, out float minimum, out float maximum)
        {
            minimum = type == typeof(int) ? 0f : 0f;
            maximum = type == typeof(int) ? 1000f : 1f;
            switch (name)
            {
                case "updateIntervalTicks": minimum = 120f; maximum = 2000f; return;
                case "unpennedJoinDistance": minimum = 8f; maximum = 60f; return;
                case "defenseScanIntervalTicks": minimum = 30f; maximum = 300f; return;
                case "protectiveBodySizeThreshold": minimum = 0.3f; maximum = 4f; return;
                case "flightDistance": minimum = 8f; maximum = 36f; return;
                case "maximumInferredHidingBodySize": minimum = 0.1f; maximum = 1f; return;
                case "defaultHideSuccessChance": minimum = 0.05f; maximum = 1f; return;
                case "hideEntryTicks": minimum = 30f; maximum = 600f; return;
                case "failedHideRetryTicks": minimum = 120f; maximum = 2500f; return;
                case "refugeRefreshIntervalTicks": minimum = 600f; maximum = 5000f; return;
                case "minimumHideTicks": minimum = 120f; maximum = 2500f; return;
                case "maximumHideTicks":
                    minimum = Mathf.Min(15000f, Mathf.Max(120f, settings.minimumHideTicks) + 300f);
                    maximum = 15000f;
                    return;
                case "basePredatorEscapeChance": minimum = 0f; maximum = 0.5f; return;
                case "predatorEscapeCheckIntervalTicks": minimum = 120f; maximum = 900f; return;
                case "hiddenPreySafeDistance": minimum = 0f; maximum = 120f; return;
                case "minimumFieldcraftSkill": minimum = 0f; maximum = 12f; return;
                case "frightenedMemoryLifetimeTicks": minimum = 60000f; maximum = 3600000f; return;
                case "packMemberKilledMemoryLifetimeTicks": minimum = 600000f; maximum = 7200000f; return;
            }
            if (name.IndexOf("Chance", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("chance", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                minimum = 0f;
                maximum = 1f;
            }
            else if (name.IndexOf("Skill", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                minimum = 0f;
                maximum = 12f;
            }
            else if (name.IndexOf("Distance", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Radius", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                minimum = 1f;
                maximum = 100f;
            }
            else if (name.IndexOf("Ticks", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                minimum = 30f;
                maximum = 7200000f;
            }
            else if (name.IndexOf("BodySize", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                minimum = 0.1f;
                maximum = 4f;
            }
            if (type == typeof(int) && maximum > 100000f) maximum = 100000f;
        }
    }

    internal sealed class WildlifeSpeciesBehaviorPresentation
    {
        private readonly InsightUiDocument document;
        private string search = string.Empty;
        private string selectedDefName;

        internal WildlifeSpeciesBehaviorPresentation()
        {
            selectedDefName = EligibleSpecies().FirstOrDefault()?.defName;
            document = new InsightUiDocument("wildlife-species-profiles", InsightUi.Empty("profiles-loading"));
            WildlifeUiTheme.Configure(document, false, false);
            document.Root = BuildRoot();
        }

        internal InsightUiDocument Document => document;

        public static void Open()
        {
            WildlifeSpeciesBehaviorPresentation presentation = new WildlifeSpeciesBehaviorPresentation();
            Find.WindowStack.Add(new InsightUiWindow(WildlifeUiText.Get("Wildlife.UI.Profiles.Title"), presentation.document));
        }

        private InsightUiElement BuildRoot()
        {
            InsightUiSearchField searchField = InsightUi.SearchField("profiles-search", search,
                WildlifeUiText.Get("Wildlife.UI.Profiles.SearchPlaceholder"), value =>
                {
                    search = value ?? string.Empty;
                    document.Root = BuildRoot();
                    document.Invalidate();
                }).Bind(() => search, value => search = value ?? string.Empty);

            List<ThingDef> species = FilteredSpecies();
            InsightUiElement speciesList;
            if (species.Count == 0)
            {
                speciesList = InsightUi.Callout("profiles-no-results", InsightUiCalloutSeverity.Info,
                    WildlifeUiText.Get("Wildlife.UI.Profiles.NoResults"),
                    WildlifeUiText.Get("Wildlife.UI.Profiles.NoResults.Help"));
            }
            else
            {
                speciesList = InsightUi.VirtualList("profiles-list-items", species.Count, 34f, index =>
                {
                    ThingDef captured = species[index];
                    InsightUiButton row = InsightUi.Button("species." + captured.defName,
                        captured.LabelCap.ToString(), () => Select(captured));
                    row.SetTooltip(WildlifeUiText.Get("Wildlife.UI.Profiles.Species.Help"));
                    row.SelectedProvider = () => selectedDefName == captured.defName;
                    return row;
                }).SetMinSize(0f, 180f);
            }

            InsightUiElement list = InsightUi.Column("profiles-list", searchField, speciesList).SetGap(8f);
            InsightUiElement detail = BuildDetail();
            InsightUiNavigation workspace = InsightUi.Navigation("profiles-responsive-master-detail", 720f)
                .Add("profiles-master", WildlifeUiText.Get("Wildlife.UI.Profiles.SpeciesList"), list)
                .Add("profiles-detail", WildlifeUiText.Get("Wildlife.UI.Profiles.Detail"), detail);
            return InsightUi.Column("profiles-root",
                InsightUi.Breadcrumbs("profiles-breadcrumbs", WildlifeUiText.Get("Wildlife.UI.Profiles.Breadcrumb")),
                InsightUi.Label("profiles-title", WildlifeUiText.Get("Wildlife.UI.Profiles.Title"), InsightUiTextStyle.Title),
                InsightUi.Label("profiles-subtitle", WildlifeUiText.Get("Wildlife.UI.Profiles.Subtitle"), InsightUiTextStyle.Caption),
                workspace).SetGap(8f).SetPadding(8f);
        }

        private InsightUiElement BuildDetail()
        {
            ThingDef selected = SelectedSpecies();
            if (selected == null)
                return InsightUi.Callout("profiles-empty", InsightUiCalloutSeverity.Info,
                    WildlifeUiText.Get("Wildlife.UI.Profiles.NoResults"), WildlifeUiText.Get("Wildlife.UI.Profiles.NoResults.Help"));

            PreyProfile defaults = PreyProfileDatabase.DefaultFor(selected);
            SpeciesBehaviorOverride item = FindOverride(selected);
            bool enabled = item?.enabled == true;
            InsightUiToggle predatorOverride = InsightUi.Toggle("profile-predator-override",
                WildlifeUiText.Get("Wildlife.UI.Profiles.PredatorOverride"), item?.hasPredatorOverride == true)
                .Bind(() => FindOverride(selected)?.hasPredatorOverride == true, value =>
                {
                    WildlifeSpeciesClassification.SetPredatorOverride(selected, value, FindOverride(selected)?.predator == true);
                    ChangedAndRebuild();
                });
            predatorOverride.Enabled = enabled;
            predatorOverride.SetTooltip(enabled ? WildlifeUiText.Get("Wildlife.UI.Profiles.PredatorOverride") :
                WildlifeUiText.Get("Wildlife.UI.Profiles.DisabledHelp"));
            InsightUiToggle preyOverride = InsightUi.Toggle("profile-prey-override",
                WildlifeUiText.Get("Wildlife.UI.Profiles.PreyOverride"), item?.hasPreyOverride == true)
                .Bind(() => FindOverride(selected)?.hasPreyOverride == true, value =>
                {
                    WildlifeSpeciesClassification.SetPreyOverride(selected, value, FindOverride(selected)?.prey != false);
                    ChangedAndRebuild();
                });
            preyOverride.Enabled = enabled;
            preyOverride.SetTooltip(enabled ? WildlifeUiText.Get("Wildlife.UI.Profiles.PreyOverride") :
                WildlifeUiText.Get("Wildlife.UI.Profiles.DisabledHelp"));
            List<InsightUiElement> controls = new List<InsightUiElement>
            {
                InsightUi.SectionHeader("profile-header", selected.LabelCap.ToString(),
                    WildlifeUiText.Get("Wildlife.UI.Profiles.DefaultSummary") + ": " +
                    WildlifeUiText.EnumValue(defaults.socialType) + " / " + WildlifeUiText.EnumValue(defaults.defenseStrategy), divider: true),
                InsightUi.Callout("profile-help", InsightUiCalloutSeverity.Info,
                    WildlifeUiText.Get("Wildlife.UI.Profiles.HelpTitle"), WildlifeUiText.Get("Wildlife.UI.Profiles.HelpBody")),
                InsightUi.Toggle("profile-enabled", WildlifeUiText.Get("Wildlife.UI.Profiles.EnableOverride"), enabled)
                    .Bind(() => FindOverride(selected)?.enabled == true, value =>
                    {
                        SpeciesBehaviorOverride target = value ? EnsureOverride(selected, defaults) : FindOverride(selected);
                        if (target != null) target.enabled = value;
                        ChangedAndRebuild();
                    }),
                predatorOverride,
                preyOverride
            };

            if (enabled)
            {
                controls.Add(BuildEnumControl("profile-social", WildlifeUiText.Get("Wildlife.UI.Profiles.Social"),
                    () => FindOverride(selected)?.socialType ?? defaults.socialType,
                    value =>
                    {
                        SpeciesBehaviorOverride target = EnsureOverride(selected, defaults);
                        target.socialType = value;
                        if (value != PreySocialType.Solitary && target.preferredGroupSize < 2)
                            target.preferredGroupSize = Math.Max(2, defaults.preferredGroupSize);
                        ChangedAndRebuild();
                    }));
                controls.Add(BuildEnumControl("profile-defense", WildlifeUiText.Get("Wildlife.UI.Profiles.Defense"),
                    () => FindOverride(selected)?.defenseStrategy ?? defaults.defenseStrategy,
                    value => { EnsureOverride(selected, defaults).defenseStrategy = value; ChangedAndRebuild(); }));
                controls.Add(BuildEnumControl("profile-refuge", WildlifeUiText.Get("Wildlife.UI.Profiles.Refuge"),
                    () => FindOverride(selected)?.refugePreference ?? defaults.refugePreference,
                    value => { EnsureOverride(selected, defaults).refugePreference = value; ChangedAndRebuild(); }));
                if ((FindOverride(selected)?.socialType ?? defaults.socialType) != PreySocialType.Solitary)
                    controls.Add(BuildProfileSlider("profile-group", WildlifeUiText.Get("Wildlife.UI.Profiles.GroupSize"), 2f, 60f,
                    () => FindOverride(selected)?.preferredGroupSize ?? defaults.preferredGroupSize,
                    value => EnsureOverride(selected, defaults).preferredGroupSize = Mathf.RoundToInt(value)));
                else
                    controls.Add(InsightUi.Label("profile-group-help", WildlifeUiText.Get("Wildlife.UI.Profiles.GroupSize.SolitaryHelp"), InsightUiTextStyle.Caption));
                controls.Add(BuildProfileSlider("profile-vigilance", WildlifeUiText.Get("Wildlife.UI.Profiles.Vigilance"), 0.05f, 0.95f,
                    () => FindOverride(selected)?.vigilanceChance ?? defaults.vigilanceChance,
                    value => EnsureOverride(selected, defaults).vigilanceChance = value));
                controls.Add(BuildProfileSlider("profile-hiding-size", WildlifeUiText.Get("Wildlife.UI.Profiles.HidingSize"), 0.1f, 4f,
                    () => FindOverride(selected)?.maximumHidingBodySize ?? defaults.maximumHidingBodySize,
                    value => EnsureOverride(selected, defaults).maximumHidingBodySize = value));
                controls.Add(BuildProfileSlider("profile-refuge-radius", WildlifeUiText.Get("Wildlife.UI.Profiles.RefugeRadius"), 6f, 50f,
                    () => FindOverride(selected)?.refugeSearchRadius ?? defaults.refugeSearchRadius,
                    value => EnsureOverride(selected, defaults).refugeSearchRadius = value));
                controls.Add(BuildProfileSlider("profile-concealment", WildlifeUiText.Get("Wildlife.UI.Profiles.Concealment"), 0.05f, 0.95f,
                    () => FindOverride(selected)?.hideSuccessChance ?? defaults.hideSuccessChance,
                    value => EnsureOverride(selected, defaults).hideSuccessChance = value));
                controls.Add(InsightUi.Button("profile-reset", WildlifeUiText.Get("Wildlife.UI.Profiles.Reset"), () =>
                {
                    HerdsMod.Settings.speciesOverrides.RemoveAll(value => value?.defName == selected.defName);
                    ChangedAndRebuild();
                }));
            }
            else
            {
                controls.Add(InsightUi.Label("profile-disabled-help", WildlifeUiText.Get("Wildlife.UI.Profiles.DisabledHelp"), InsightUiTextStyle.Caption));
            }
            return InsightUi.Scroll("profile-detail-scroll", InsightUi.Column("profile-detail-content", controls.ToArray()).SetGap(8f));
        }

        private static InsightUiElement BuildEnumControl<T>(string id, string label, Func<T> getter, Action<T> setter) where T : struct
        {
            T[] values = (T[])Enum.GetValues(typeof(T));
            string[] options = values.Select(WildlifeUiText.EnumValue).ToArray();
            return InsightUi.Select(id, label, options, 0).Bind(
                () => Array.IndexOf(values, getter()),
                index => setter(values[Mathf.Clamp(index, 0, values.Length - 1)]));
        }

        private InsightUiElement BuildProfileSlider(string id, string label, float minimum, float maximum,
            Func<float> getter, Action<float> setter)
        {
            InsightUiSlider slider = InsightUi.Slider(id + ".value", getter(), minimum, maximum).Bind(getter, value =>
            {
                setter(value);
                ChangedAndRebuild();
            });
            InsightUiLabel value = InsightUi.Label(id + ".label", string.Empty).SetTextProvider(() =>
                label + ": " + getter().ToString("0.##", CultureInfo.InvariantCulture));
            return InsightUi.Column(id, value, slider).SetGap(3f);
        }

        private void Select(ThingDef species)
        {
            selectedDefName = species?.defName;
            document.Root = BuildRoot();
            document.Invalidate();
        }

        private List<ThingDef> FilteredSpecies()
        {
            return EligibleSpecies().Where(def => string.IsNullOrWhiteSpace(search) ||
                def.LabelCap.ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                def.defName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        private ThingDef SelectedSpecies() => EligibleSpecies().FirstOrDefault(def => def.defName == selectedDefName);

        private static List<ThingDef> EligibleSpecies()
        {
            return DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => PreyProfileDatabase.DefaultFor(def)?.eligible == true)
                .OrderBy(def => def.LabelCap.ToString()).ToList();
        }

        private static SpeciesBehaviorOverride FindOverride(ThingDef species)
        {
            return HerdsMod.Settings?.speciesOverrides?.FirstOrDefault(value => value?.defName == species?.defName);
        }

        private static SpeciesBehaviorOverride EnsureOverride(ThingDef species, PreyProfile defaults)
        {
            SpeciesBehaviorOverride item = FindOverride(species);
            if (item != null) return item;
            item = SpeciesBehaviorOverride.FromProfile(defaults);
            HerdsMod.Settings.speciesOverrides.Add(item);
            return item;
        }

        private void ChangedAndRebuild()
        {
            PreyProfileDatabase.Clear();
            WildlifeNicheDatabase.Clear();
            if (Current.Game?.Maps != null)
                for (int i = 0; i < Current.Game.Maps.Count; i++)
                    Current.Game.Maps[i].GetComponent<HerdMapComponent>()?.ForceRefresh();
            HerdsMod.Instance?.PersistExternalSettings();
            document.Root = BuildRoot();
            document.Invalidate();
        }
    }
}
