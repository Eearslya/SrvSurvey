﻿using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SrvSurvey.game;
using SrvSurvey.net;
using System.Diagnostics;

namespace SrvSurvey
{
    /// <summary>
    /// Represents a class of Guardian Ruin or Structure. Eg: Alpha, Beta, Fistbump, etc
    /// </summary>
    class GuardianSiteTemplate
    {
        #region static loading code

        public static readonly Dictionary<GuardianSiteData.SiteType, GuardianSiteTemplate> sites = new Dictionary<GuardianSiteData.SiteType, GuardianSiteTemplate>();

        public static string filename = "guardianSiteTemplates.json";
        private static string editableFilepath = Path.Combine(Program.dataFolder, GuardianSiteTemplate.filename);
        private static string pubDataFilepath = Path.Combine(Git.pubDataFolder, GuardianSiteTemplate.filename);

        public static void Import(bool devReload = false)
        {
            string filepath;
            if (devReload && Debugger.IsAttached)
            {
                Game.log($"Using {GuardianSiteTemplate.filename}, devReload:{devReload}");
                filepath = Path.Combine(Git.srcRootFolder, "SrvSurvey", GuardianSiteTemplate.filename);
            }
            else if (File.Exists(editableFilepath))
            {
                // load map editor version?
                Game.log($"Using {GuardianSiteTemplate.filename} from editor");
                filepath = editableFilepath;
            }
            else if (File.Exists(pubDataFilepath))
            {
                // load pub data version?
                Game.log($"Using {GuardianSiteTemplate.filename} from pubData");
                filepath = pubDataFilepath;
            }
            else
            {
                // otherwise, use the file shipped with the app
                Game.log($"Using {GuardianSiteTemplate.filename} app package");
                filepath = Path.Combine(Application.StartupPath, GuardianSiteTemplate.filename);
            }

            Game.log($"Reading {GuardianSiteTemplate.filename}: {filepath}");
            if (File.Exists(filepath))
            {
                using (var reader = new StreamReader(new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    var json = reader.ReadToEnd();
                    var newSites = JsonConvert.DeserializeObject<Dictionary<GuardianSiteData.SiteType, GuardianSiteTemplate>>(json)!;
                    foreach (var _ in newSites)
                    {
                        _.Value.init();
                        GuardianSiteTemplate.sites[_.Key] = _.Value;
                    }

                    Game.log($"SiteTemplate.Imported {GuardianSiteTemplate.sites.Count} templates");
                }
            }
            else
            {
                Game.log($"Missing file: {filepath}");
            }
        }

        public static void SaveEdits()
        {
            Game.log($"Saving edits to SiteTemplates: {editableFilepath}");

            // alpha sort all POIs by their name, and remove any destructablePanel
            foreach (var template in GuardianSiteTemplate.sites.Values)
                template.poi = template.poi
                    .OrderBy(_ => _.sortName)
                    .Where(_ => _.type != POIType.destructablePanel)
                    .ToList();

            var json = JsonConvert.SerializeObject(GuardianSiteTemplate.sites, Formatting.Indented);
            File.WriteAllText(editableFilepath, json);

            // restore the destructablePanel's
            foreach (var template in GuardianSiteTemplate.sites.Values)
                template.init();
        }

        public static void publish()
        {
            if (File.Exists(editableFilepath))
            {
                var srcFilepath = Path.GetFullPath(Path.Combine(Git.srcRootFolder, "SrvSurvey", GuardianSiteTemplate.filename));
                var oldContent = File.ReadAllText(srcFilepath);
                var newContent = File.ReadAllText(editableFilepath);
                if (oldContent != newContent)
                {
                    Game.log($"Publishing edited {GuardianSiteTemplate.filename}");
                    File.Copy(editableFilepath, srcFilepath, true);

                    // and copy to the original file name (back-compat for older builds)
                    var srcFilepathOld = Path.GetFullPath(Path.Combine(Git.srcRootFolder, "SrvSurvey", "settlementTemplates.json"));
                    File.Copy(editableFilepath, srcFilepathOld, true);

                    Git.updateDevGitData(_ => _.settlementTemplate++);
                }
            }

            GuardianSiteTemplate.Import(true);
        }

        #endregion

        #region instance members loaded from JSON

        /// <summary> The class/type name of this Guardian site. Eg: Alpha, Beta, Fistbump, etc </summary>
        public string name = "";

        /// <summary> The filename of the background image to use.</summary>
        public string backgroundImage = "";

        /// <summary> Offset applied to bring the site origin location to the center of the image </summary>
        public Point imageOffset = Point.Empty;

        /// <summary> The scale factor for the background image, adjusting that 1 pixel is 1 meter. </summary>
        public float scaleFactor = 1;

        public List<SitePOI> poi = new List<SitePOI>();

        public Dictionary<string, PointF> obeliskGroupNameLocations = new Dictionary<string, PointF>();

        /// <summary> Destructable panels </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public List<SitePOI>? destructablePanels;

        #endregion

        private void init()
        {
            this.relicTowerNames = new List<string>();
            this.poiObelisks = new List<SitePOI>();
            this.poiSurvey = new List<SitePOI>();
            this.poiRelics = new List<SitePOI>();

            foreach (var _ in this.poi)
            {
                if (_.type == POIType.obelisk || _.type == POIType.brokeObelisk)
                {
                    this.poiObelisks.Add(_);
                    continue;
                }

                this.poiSurvey.Add(_);
                if (_.type == POIType.relic)
                {
                    this.poiRelics.Add(_);
                    this.relicTowerNames.Add(_.name);
                }

                if (Util.isBasicPoi(_.type) || _.type == POIType.emptyPuddle)
                    this.countPuddles++;
            }

            if (Game.settings.guardianComponentMaterials_TEST && this.destructablePanels?.Count > 0)
                foreach (var _ in this.destructablePanels)
                    this.poi.Add(_);
        }

        [JsonIgnore]
        public List<string> relicTowerNames { get; private set; }

        [JsonIgnore]
        public List<SitePOI> poiObelisks;

        /// <summary>
        /// POI without obelisk or brokeObelisk
        /// </summary>
        [JsonIgnore]
        public List<SitePOI> poiSurvey;

        [JsonIgnore]
        public List<SitePOI> poiRelics;

        [JsonIgnore]
        public int countPuddles { get; private set; }

        public string nextNameForNewPoi(POIType poiType)
        {
            var prefix = getPoiPrefix(poiType);

            var lastEntryOfType = this.poi
                .Where(_ => _.name.StartsWith(prefix) && char.IsAsciiDigit(_.name.Substring(prefix.Length)[0]))
                .OrderBy(_ => int.Parse(_.name.Substring(prefix.Length)))
                .LastOrDefault()?.name;

            var nextIdx = lastEntryOfType == null
                ? 1 : int.Parse(lastEntryOfType.Substring(prefix.Length)) + 1;

            return $"{prefix}{nextIdx}";
        }

        public static string getPoiPrefix(POIType poiType)
        {
            switch (poiType)
            {
                case POIType.relic:
                    return "t";
                case POIType.casket:
                case POIType.orb:
                case POIType.tablet:
                case POIType.totem:
                case POIType.urn:
                case POIType.unknown:
                    return "p";

                case POIType.pylon:
                    return "py";

                case POIType.component:
                    return "c";

                case POIType.destructablePanel:
                    return "d";

                default:
                    throw new Exception($"Unexpected poiType: '{poiType}'");
            }
        }

        public SitePOI? findPoiAtLocation(decimal angle, decimal dist, POIType poiType, bool matchPoiType, List<SitePOI>? rawPoi = null)
        {
            var pois = rawPoi == null ? this.poi : this.poi.Union(rawPoi);
            foreach (var poi in pois)
            {
                // exact matches are not interesting
                if (poi.angle == angle && poi.dist == dist)
                {
                    // unless the type does not match
                    if (poi.type != poiType) { Game.log("Exact match of different types!?"); }
                    return poi;
                }

                // TODO: curate angle based on the distance? (Otherwise things far away might be deemed too close when they're not)

                // slightly close, of the same type
                if (poi.type == poiType && Util.isClose(poi.angle, angle, 3) && Util.isClose(poi.dist, dist, 10))
                {
                    Game.log($"Match slightly close? {poiType}: a:{angle} vs {poi.angle}, d:{dist} vs {poi.dist}");
                    return poi;
                }

                // really close of any type
                var ta = 1m;
                if (Util.isClose(poi.angle, angle, ta) && Util.isClose(poi.dist, dist, 3))
                {
                    Game.log($"Match really close? {poiType} vs {poi.type}, a:{angle} vs {poi.angle}, d:{dist} vs {poi.dist}");
                    Debugger.Break();
                    return poi;
                }
            }

            // otherwise no match
            return null;
        }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    enum POIType
    {
        unknown = 0,
        relic,
        orb,
        casket,
        tablet,
        totem,
        urn,
        emptyPuddle, // TODO: remove?
        component,
        pylon,
        obelisk,
        brokeObelisk,
        // new in v2.0.2.+7
        destructablePanel,

        /* Possibilities for future use:
         * ObeliskGroup
         * AncientBeacon
         * SkimmerSpawnPoint
         */
    }

    [JsonConverter(typeof(StringEnumConverter))]
    enum ObeliskItem
    {
        unknown = 0,
        casket,
        orb,
        relic,
        tablet,
        totem,
        urn,
        sensor,
        probe,
        link,
        cyclops,
        basilisk,
        medusa,
    }

    [JsonConverter(typeof(StringEnumConverter))]
    enum ObeliskData
    {
        unknown = 0,
        alpha,
        beta,
        epsilon,
        delta,
        gamma,
    }

    /// <summary> Some item or location expected within a Guardian site. </summary>
    [JsonConverter(typeof(SitePOI.JsonConverter))]
    class SitePOI
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public POIType type;
        public string name;
        public decimal angle;
        public decimal dist;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public decimal rot;

        public override string ToString()
        {
            return $"{type} {name} {angle}° {dist}m rot:{rot}°";
        }

        [JsonIgnore]
        public string displayType
        {
            get
            {
                var foo = this.type.ToString();
                foo = foo[0].ToString().ToUpperInvariant() + foo.Substring(1);
                return foo;
            }
        }

        [JsonIgnore]
        public string sortName
        {
            get
            {
                // make obelisks always be sorted to the top
                if (this.type == POIType.obelisk || this.type == POIType.brokeObelisk)
                    return "_" + name;

                // make the number parts always be padded with 2 zero's 
                var idx = GuardianSiteTemplate.getPoiPrefix(this.type).Length;
                return name.Substring(0, idx) + name.Substring(idx).PadLeft(2, '0');
            }
        }

        class JsonConverter : Newtonsoft.Json.JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return false;
            }

            public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            {
                var poi = new SitePOI();

                reader.Read();
                if (reader.Value as string == "name")
                    poi.name = reader.ReadAsString()!;
                else
                    poi.type = reader.ValueType == typeof(string)
                        ? Enum.Parse<POIType>(reader.ReadAsString()!)
                        : (POIType)reader.ReadAsInt32()!;

                reader.Read();
                if (reader.Value as string == "name")
                    poi.name = reader.ReadAsString()!;
                else
                    poi.type = reader.ValueType == typeof(string)
                        ? Enum.Parse<POIType>(reader.ReadAsString()!)
                        : (POIType)reader.ReadAsInt32()!;

                reader.Read();
                poi.angle = reader.ReadAsDecimal()!.Value;

                reader.Read();
                poi.dist = reader.ReadAsDecimal()!.Value;

                reader.Read();
                if (reader.TokenType != JsonToken.EndObject)
                {
                    poi.rot = reader.ReadAsDecimal()!.Value;
                    reader.Read();
                }

                //var obj = serializer.Deserialize<JObject>(reader);
                //if (obj == null || !obj.HasValues)
                //    return null;

                //var type = obj["type"]!.Type == JTokenType.String
                //    ? Enum.Parse<POIType>(obj["type"]!.Value<string>()!, true)
                //    : (POIType)obj["type"]!.Value<int>();

                //var poi = new SitePOI()
                //{
                //    name = obj["name"]!.Value<string>()!,
                //    angle = obj["angle"]!.Value<decimal>(),
                //    dist = obj["dist"]!.Value<decimal>(),
                //    rot = obj["rot"]!.Value<decimal>(),
                //    type = type,
                //};

                return poi;
            }

            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            {
                var poi = value as SitePOI;

                if (poi == null)
                    throw new Exception($"Unexpected value: {value?.GetType().Name}");

                var json = $"\"name\": \"{poi.name}\", \"type\": \"{poi.type}\", \"angle\": {poi.angle}, \"dist\": {poi.dist}";
                if (poi.rot > 0 && (poi.type == POIType.pylon || poi.type == POIType.component || poi.type == POIType.relic || poi.type == POIType.obelisk || poi.type == POIType.brokeObelisk))
                    json += $", \"rot\": {poi.rot}";
                writer.WriteRawValue("{ " + json + " }");

                //var obj = new JObject();
                //obj.Add("type", poi.type.ToString());
                //obj.Add("name", poi.name);
                //obj.Add("angle", poi.angle);
                //obj.Add("dist", (decimal)poi.dist);
                //obj.Add("rot", poi.rot);
                //obj.WriteTo(writer);
            }
        }

    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum SitePoiStatus
    {
        unknown,
        /// <summary> The POI has been confirmed present in some site. </summary>
        present,
        /// <summary> The POI has been confirmed NOT present in some site. </summary>
        absent,
        /// <summary> The POI has been confirmed present but with no item. Only applies to Puddles. </summary>
        empty,
    }
}
