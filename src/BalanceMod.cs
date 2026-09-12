using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Il2CppBrokenArrow.DataBase.Models;
using Il2CppBrokenArrow.Shared.Ecs;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(BABalance.Core), "BA Balance", "1.2.0", "BovineOverlord")]
[assembly: MelonGame(null, null)]

namespace BABalance
{
    public class Core : MelonMod
    {
        private static bool exported;
        private static bool loadedOv;
        private static int frame;
        private static int applyTicks;

        // SAFE-1: null = unknown, true/false = whether EasyAntiCheat is loaded in this process.
        private static bool? _eacActive;
        private static bool _eacWarned;

        private static readonly List<Dictionary<string, JsonElement>> ovUnits = new List<Dictionary<string, JsonElement>>();
        private static readonly List<Dictionary<string, JsonElement>> ovAmmo = new List<Dictionary<string, JsonElement>>();

        private static string ROOT => Path.Combine(MelonEnvironment.GameRootDirectory, "_BAMod");
        private static string OUT => Path.Combine(ROOT, "dump");
        private static string OVR => Path.Combine(ROOT, "overrides.json");

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("BA Balance v1.1 init");
        }

        // ---------- json helpers ----------
        private static bool Has(Dictionary<string, JsonElement> d, string k) => d.ContainsKey(k);

        private static int VI(JsonElement v) => v.TryGetInt32(out var i) ? i : (int)v.GetDouble();
        private static float VF(JsonElement v) => (float)v.GetDouble();

        private static void LoadOverrides()
        {
            loadedOv = true;
            try
            {
                if (!File.Exists(OVR)) { MelonLogger.Warning("no overrides.json at " + OVR); return; }
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(OVR));
                JsonElement root = doc.RootElement;
                Grab(root, "units", ovUnits);
                Grab(root, "ammunitions", ovAmmo);
                MelonLogger.Msg("overrides loaded: " + ovUnits.Count + " units, " + ovAmmo.Count + " ammo");
            }
            catch (Exception ex) { MelonLogger.Error("overrides parse: " + ex.Message); }
        }

        private static void Grab(JsonElement root, string sec, List<Dictionary<string, JsonElement>> into)
        {
            if (!root.TryGetProperty(sec, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            foreach (JsonElement item in arr.EnumerateArray())
            {
                var d = new Dictionary<string, JsonElement>();
                foreach (JsonProperty p in item.EnumerateObject()) d[p.Name] = p.Value.Clone();
                into.Add(d);
            }
        }

        // ---------- SAFE-1: refuse to touch stats while anti-cheat is loaded ----------
        // Modified unit stats are server-checked online and will get the account banned. If the
        // game was launched through EasyAntiCheat (Steam Play / EACLauncher) rather than the offline
        // modded launcher, EAC's module is loaded in this process. In that case we apply nothing.
        private static bool AntiCheatActive()
        {
            if (_eacActive.HasValue) return _eacActive.Value;
            bool found = false;
            try
            {
                foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
                {
                    string n = null;
                    try { n = m.ModuleName; } catch { }
                    if (string.IsNullOrEmpty(n)) continue;
                    string ln = n.ToLowerInvariant();
                    if (ln.Contains("easyanticheat") || ln.StartsWith("eac_") || ln == "eac.dll")
                    {
                        found = true;
                        break;
                    }
                }
            }
            catch { found = false; } // if we cannot tell, don't block the user's offline use
            _eacActive = found;
            return found;
        }

        // ---------- unit resolution ----------
        // BUG-3: a unit's shown price is OriginalCost + Sum(loadout Option.Cost), and a loadout Option
        // can carry ReplaceUnitId that deploys a DIFFERENT unit whose OriginalCost is what gets read.
        // Infantry are always bought through such a loadout, so editing the base squad alone does nothing.
        // We therefore also target every unit reachable via BaseUnit and via option ReplaceUnitId.
        private static void CollectTargets(DataBaseService svc, Dictionary<string, JsonElement> o, List<Units> outList)
        {
            var seen = new HashSet<int>();
            void Add(Units u)
            {
                if (u == null) return;
                int uid; try { uid = u.Id; } catch { uid = -1; }
                if (uid >= 0 && !seen.Add(uid)) return;
                outList.Add(u);
            }

            if (o.TryGetValue("id", out var idv) && idv.TryGetInt32(out var id))
            {
                Add(TryGetUnit(svc, id));
            }
            else if (o.TryGetValue("name", out var nv) && nv.ValueKind == JsonValueKind.String)
            {
                string want = nv.GetString().Trim().ToLowerInvariant();
                for (int i = 0; i <= 700; i++)
                {
                    Units u = TryGetUnit(svc, i);
                    string nm = null;
                    try { nm = u?.Name; } catch { }
                    if (u != null && nm != null && nm.Trim().ToLowerInvariant() == want) Add(u);
                }
            }

            // Expand transitively over BaseUnit + option ReplaceUnitId (dedup guards termination).
            for (int i = 0; i < outList.Count; i++)
            {
                Units u = outList[i];
                try { Add(u.BaseUnit); } catch { }
                EnumOptions(u, op =>
                {
                    int rep; try { rep = op.ReplaceUnitId; } catch { rep = 0; }
                    if (rep > 0) Add(TryGetUnit(svc, rep));
                });
            }
        }

        // Visit every loadout Option of a unit (Modifications -> Options, plus CurrentOptions).
        private static void EnumOptions(Units u, Action<Options> visit)
        {
            try
            {
                var mods = u.Modifications;
                int mn = 0; try { mn = mods != null ? mods.Count : 0; } catch { mn = 0; }
                for (int i = 0; i < mn; i++)
                {
                    Modifications m = null; try { m = mods[i]; } catch { }
                    if (m == null) continue;
                    var opts = m.Options;
                    int on = 0; try { on = opts != null ? opts.Count : 0; } catch { on = 0; }
                    for (int j = 0; j < on; j++)
                    {
                        Options op = null; try { op = opts[j]; } catch { }
                        if (op != null) visit(op);
                    }
                }
            }
            catch { }
            try
            {
                var cur = u.CurrentOptions;
                int cn = 0; try { cn = cur != null ? cur.Count : 0; } catch { cn = 0; }
                for (int j = 0; j < cn; j++)
                {
                    Options op = null; try { op = cur[j]; } catch { }
                    if (op != null) visit(op);
                }
            }
            catch { }
        }

        private static Units TryGetUnit(DataBaseService svc, int id)
        {
            try { return svc.GetUnitById(id, false); } catch { return null; }
        }

        // ---------- armor writing (BUG-4/5: write EVERY armor object, not just the display one) ----------
        private static bool ApplyArmorFields(Armors a, Dictionary<string, JsonElement> o)
        {
            if (a == null) return false;
            bool any = false;
            try
            {
                if (Has(o, "HP")) { a.MaxHealthPoints = VI(o["HP"]); any = true; }
                if (Has(o, "ArmorValue")) { a.ArmorValue = VI(o["ArmorValue"]); any = true; }
                if (Has(o, "KinF")) { a.KinArmorFront = VI(o["KinF"]); any = true; }
                if (Has(o, "KinS")) { a.KinArmorSides = VI(o["KinS"]); any = true; }
                if (Has(o, "KinR")) { a.KinArmorRear = VI(o["KinR"]); any = true; }
                if (Has(o, "KinT")) { a.KinArmorTop = VI(o["KinT"]); any = true; }
                if (Has(o, "HeatF")) { a.HeatArmorFront = VI(o["HeatF"]); any = true; }
                if (Has(o, "HeatS")) { a.HeatArmorSides = VI(o["HeatS"]); any = true; }
                if (Has(o, "HeatR")) { a.HeatArmorRear = VI(o["HeatR"]); any = true; }
                if (Has(o, "HeatT")) { a.HeatArmorTop = VI(o["HeatT"]); any = true; }
            }
            catch { }
            return any;
        }

        private static bool HasAnyArmorKey(Dictionary<string, JsonElement> o)
        {
            return Has(o, "HP") || Has(o, "ArmorValue")
                || Has(o, "KinF") || Has(o, "KinS") || Has(o, "KinR") || Has(o, "KinT")
                || Has(o, "HeatF") || Has(o, "HeatS") || Has(o, "HeatR") || Has(o, "HeatT");
        }

        private static bool ApplyToUnit(Units u, Dictionary<string, JsonElement> o)
        {
            bool changed = false;
            try
            {
                if (Has(o, "Cost"))
                {
                    int c = VI(o["Cost"]);
                    try { u.Cost = c; changed = true; } catch { }
                    try { u.OriginalCost = c; } catch { }
                }
                if (Has(o, "Stealth"))
                {
                    try { u.Stealth = VF(o["Stealth"]); changed = true; } catch { }
                }

                if (HasAnyArmorKey(o))
                {
                    // Write the active/display armor AND every armor loadout entry. Ground vehicles
                    // read their combat armor from a list entry, not from the single display object,
                    // which is why editing only .Armor showed a "phantom" value with no real effect.
                    try { if (ApplyArmorFields(u.Armor, o)) changed = true; } catch { }
                    try { if (ApplyArmorFields(u.CurrentArmor, o)) changed = true; } catch { }
                    try
                    {
                        var list = u.Armors;
                        int n = 0;
                        try { n = list != null ? list.Count : 0; } catch { n = 0; }
                        for (int i = 0; i < n; i++)
                        {
                            Armors a = null;
                            try { a = list[i]; } catch { }
                            if (ApplyArmorFields(a, o)) changed = true;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex) { MelonLogger.Error("unit apply: " + ex.Message); }
            return changed;
        }

        private static int ApplyUnits(DataBaseService svc)
        {
            int num = 0;
            var targets = new List<Units>();
            foreach (var o in ovUnits)
            {
                targets.Clear();
                CollectTargets(svc, o, targets);
                if (targets.Count == 0) continue;
                bool changed = false;
                foreach (Units u in targets) changed |= ApplyToUnit(u, o);
                if (changed) num++;
            }
            return num;
        }

        private static int ApplyAmmo(DataBaseService svc)
        {
            int num = 0;
            foreach (var item in ovAmmo)
            {
                Ammunitions val = null;
                if (item.TryGetValue("id", out var idv) && idv.TryGetInt32(out var id))
                {
                    try { val = svc.GetAmmunitionById(id); } catch { }
                }
                if (val == null) continue;
                try
                {
                    if (Has(item, "Damage")) { val.Damage = VF(item["Damage"]); num++; }
                    if (Has(item, "PenMin")) { val.PenetrationAtMinRange = VF(item["PenMin"]); num++; }
                    if (Has(item, "PenGround")) { val.PenetrationAtGroundRange = VF(item["PenGround"]); num++; }
                    if (Has(item, "SupplyCost")) { val.SupplyCost = VF(item["SupplyCost"]); num++; }
                    if (Has(item, "ResupplyTime")) { val.ResupplyTime = VF(item["ResupplyTime"]); num++; }
                }
                catch (Exception ex) { MelonLogger.Error("ammo apply: " + ex.Message); }
            }
            return num;
        }

        public override void OnUpdate()
        {
            if (++frame % 120 != 0) return;

            DataBaseService svc;
            try
            {
                svc = DataBaseService._instance;
                if (svc == null || !svc.IsLoaded) return;
            }
            catch { return; }

            if (!loadedOv) LoadOverrides();

            // Export is read-only, always allowed (helps users find ids/values).
            if (!exported)
            {
                exported = true;
                try { Directory.CreateDirectory(OUT); } catch { }
                Exporter.ExportAll(svc, OUT);
            }

            // SAFE-1: never modify stats while anti-cheat is loaded (online = bannable).
            if (AntiCheatActive())
            {
                if (!_eacWarned)
                {
                    _eacWarned = true;
                    MelonLogger.Warning("==================================================================");
                    MelonLogger.Warning(" EasyAntiCheat is loaded -> stat overrides DISABLED for safety.");
                    MelonLogger.Warning(" Playing online with modified stats gets accounts BANNED.");
                    MelonLogger.Warning(" Launch with 'Launch Broken Arrow (Modded).bat' (anti-cheat off)");
                    MelonLogger.Warning(" to use overrides. Never use Steam Play / EACLauncher with mods.");
                    MelonLogger.Warning("==================================================================");
                }
                return;
            }

            int nu = ApplyUnits(svc);
            int na = ApplyAmmo(svc);
            if (nu + na > 0)
            {
                applyTicks++;
                if (applyTicks <= 3) MelonLogger.Msg("applied overrides: units=" + nu + " ammo=" + na);
            }
        }
    }

    internal static class Exporter
    {
        private static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
        }

        private static int GI(Func<int> f) { try { return f(); } catch { return 0; } }
        private static string GS(Func<string> f) { try { return f(); } catch { return ""; } }
        private static float GF(Func<float> f) { try { return f(); } catch { return 0f; } }

        public static void ExportAll(DataBaseService inst, string OUT)
        {
            try
            {
                var sb = new StringBuilder("[\n");
                bool first = true;
                int num = 0;
                for (int i = 0; i <= 700; i++)
                {
                    Units u = null;
                    try { u = inst.GetUnitById(i, false); } catch { }
                    if (u == null) continue;
                    num++;

                    string name = GS(() => u.Name);
                    string type = GS(() => ((object)u.Type).ToString());
                    int cost = GI(() => u.Cost);
                    int origCost = GI(() => u.OriginalCost);
                    int baseId = GI(() => u.BaseUnit != null ? u.BaseUnit.Id : -1);
                    int infId = GI(() => u.OwnerInfantryID);

                    int hp = 0, av = 0, kf = 0, ks = 0, kr = 0, kt = 0, hf = 0, hs = 0, hr = 0, ht = 0, armorCount = 0;
                    try
                    {
                        var list = u.Armors;
                        try { armorCount = list != null ? list.Count : 0; } catch { armorCount = 0; }
                    }
                    catch { }
                    try
                    {
                        Armors a = u.Armor;
                        if (a != null)
                        {
                            hp = GI(() => a.MaxHealthPoints); av = GI(() => a.ArmorValue);
                            kf = GI(() => a.KinArmorFront); ks = GI(() => a.KinArmorSides);
                            kr = GI(() => a.KinArmorRear); kt = GI(() => a.KinArmorTop);
                            hf = GI(() => a.HeatArmorFront); hs = GI(() => a.HeatArmorSides);
                            hr = GI(() => a.HeatArmorRear); ht = GI(() => a.HeatArmorTop);
                        }
                    }
                    catch { }

                    if (!first) sb.Append(",\n");
                    first = false;
                    sb.Append("  {\"id\":").Append(i)
                      .Append(",\"Name\":\"").Append(Esc(name)).Append("\"")
                      .Append(",\"Type\":\"").Append(Esc(type)).Append("\"")
                      .Append(",\"BaseUnitId\":").Append(baseId)
                      .Append(",\"OwnerInfantryID\":").Append(infId)
                      .Append(",\"ArmorEntries\":").Append(armorCount)
                      .Append(",\"Cost\":").Append(cost)
                      .Append(",\"OrigCost\":").Append(origCost)
                      .Append(",\"HP\":").Append(hp)
                      .Append(",\"ArmorValue\":").Append(av)
                      .Append(",\"KinF\":").Append(kf).Append(",\"KinS\":").Append(ks)
                      .Append(",\"KinR\":").Append(kr).Append(",\"KinT\":").Append(kt)
                      .Append(",\"HeatF\":").Append(hf).Append(",\"HeatS\":").Append(hs)
                      .Append(",\"HeatR\":").Append(hr).Append(",\"HeatT\":").Append(ht)
                      .Append("}");
                }
                sb.Append("\n]\n");
                File.WriteAllText(Path.Combine(OUT, "Units_full.json"), sb.ToString());
                MelonLogger.Msg("exported Units_full.json (" + num + ")");
            }
            catch (Exception ex) { MelonLogger.Error("units export: " + ex.Message); }

            try { ExportOptions(inst, OUT); } catch (Exception ex) { MelonLogger.Error("options export: " + ex.Message); }

            try
            {
                var sb = new StringBuilder("[\n");
                bool first = true;
                int num = 0;
                for (int j = 0; j <= 4000; j++)
                {
                    Ammunitions a = null;
                    try { a = inst.GetAmmunitionById(j); } catch { }
                    if (a == null) continue;
                    num++;
                    string name = GS(() => a.Name);
                    float dmg = GF(() => a.Damage);
                    float pmin = GF(() => a.PenetrationAtMinRange);
                    float pgr = GF(() => a.PenetrationAtGroundRange);
                    float sc = GF(() => a.SupplyCost);
                    float rt = GF(() => a.ResupplyTime);
                    float gr = GF(() => a.GroundRange);
                    if (!first) sb.Append(",\n");
                    first = false;
                    sb.Append("  {\"id\":").Append(j)
                      .Append(",\"Name\":\"").Append(Esc(name)).Append("\"")
                      .Append(",\"Damage\":").Append(dmg)
                      .Append(",\"PenMin\":").Append(pmin)
                      .Append(",\"PenGround\":").Append(pgr)
                      .Append(",\"SupplyCost\":").Append(sc)
                      .Append(",\"ResupplyTime\":").Append(rt)
                      .Append(",\"GroundRange\":").Append(gr)
                      .Append("}");
                }
                sb.Append("\n]\n");
                File.WriteAllText(Path.Combine(OUT, "Ammunitions_full.json"), sb.ToString());
                MelonLogger.Msg("exported Ammunitions_full.json (" + num + ")");
            }
            catch (Exception ex) { MelonLogger.Error("ammo export: " + ex.Message); }
        }

        // Diagnostic: per-unit loadout options, so we can see where a unit's price actually lives
        // (a default option's Cost, or a ReplaceUnitId that deploys a different unit). Only units
        // that have options are listed.
        public static void ExportOptions(DataBaseService inst, string OUT)
        {
            var sb = new StringBuilder("[\n");
            bool first = true;
            for (int i = 0; i <= 700; i++)
            {
                Units u = null;
                try { u = inst.GetUnitById(i, false); } catch { }
                if (u == null) continue;

                var lines = new List<string>();
                void Emit(Options op, string src)
                {
                    if (op == null) return;
                    int oid = GI(() => op.Id);
                    int oc = GI(() => op.Cost);
                    bool def = false; try { def = op.IsDefault; } catch { }
                    int rep = GI(() => op.ReplaceUnitId);
                    int mod = GI(() => op.ModificationId);
                    lines.Add("{\"src\":\"" + src + "\",\"id\":" + oid + ",\"Cost\":" + oc
                        + ",\"def\":" + (def ? "true" : "false") + ",\"rep\":" + rep + ",\"mod\":" + mod + "}");
                }

                try
                {
                    var mods = u.Modifications;
                    int mn = 0; try { mn = mods != null ? mods.Count : 0; } catch { mn = 0; }
                    for (int a = 0; a < mn; a++)
                    {
                        Modifications m = null; try { m = mods[a]; } catch { }
                        if (m == null) continue;
                        var opts = m.Options;
                        int on = 0; try { on = opts != null ? opts.Count : 0; } catch { on = 0; }
                        for (int b = 0; b < on; b++) { Options op = null; try { op = opts[b]; } catch { } Emit(op, "mod"); }
                    }
                }
                catch { }
                try
                {
                    var cur = u.CurrentOptions;
                    int cn = 0; try { cn = cur != null ? cur.Count : 0; } catch { cn = 0; }
                    for (int b = 0; b < cn; b++) { Options op = null; try { op = cur[b]; } catch { } Emit(op, "cur"); }
                }
                catch { }

                if (lines.Count == 0) continue;
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("  {\"id\":").Append(i)
                  .Append(",\"Name\":\"").Append(Esc(GS(() => u.Name))).Append("\"")
                  .Append(",\"Type\":\"").Append(Esc(GS(() => ((object)u.Type).ToString()))).Append("\"")
                  .Append(",\"OrigCost\":").Append(GI(() => u.OriginalCost))
                  .Append(",\"Opts\":[").Append(string.Join(",", lines)).Append("]}");
            }
            sb.Append("\n]\n");
            File.WriteAllText(Path.Combine(OUT, "Units_options.json"), sb.ToString());
            MelonLogger.Msg("exported Units_options.json");
        }
    }
}
