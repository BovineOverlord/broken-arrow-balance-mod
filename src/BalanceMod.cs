using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using MelonLoader;
using MelonLoader.Utils;
using Il2CppBrokenArrow.Shared.Ecs;
using Il2CppBrokenArrow.DataBase.Models;

[assembly: MelonInfo(typeof(BABalance.Core), "BA Balance", "1.0.0", "BovineOverlord")]
[assembly: MelonGame(null, null)]

namespace BABalance
{
    // v1.0: reads _BAMod/overrides.json and applies it to the live decrypted DB.
    // Also re-exports the current DB to _BAMod/dump for reference.
    public class Core : MelonMod
    {
        static bool exported = false;
        static bool loadedOv = false;
        static int frame = 0;
        static int applyTicks = 0;
        static string ROOT => System.IO.Path.Combine(MelonEnvironment.GameRootDirectory, "_BAMod");
        static string OUT => Path.Combine(ROOT, "dump");
        static string OVR => Path.Combine(ROOT, "overrides.json");

        // parsed overrides
        static List<Dictionary<string, JsonElement>> ovUnits = new List<Dictionary<string, JsonElement>>();
        static List<Dictionary<string, JsonElement>> ovAmmo = new List<Dictionary<string, JsonElement>>();

        public override void OnInitializeMelon() { LoggerInstance.Msg("BA Balance v1.0 init"); }

        static int GI(Dictionary<string, JsonElement> d, string k, int def)
        { return d.TryGetValue(k, out var v) && v.TryGetInt32(out int n) ? n : def; }
        static bool Has(Dictionary<string, JsonElement> d, string k) { return d.ContainsKey(k); }
        static int VI(JsonElement v) { return v.TryGetInt32(out int n) ? n : (int)v.GetDouble(); }
        static float VF(JsonElement v) { return (float)v.GetDouble(); }

        static void LoadOverrides()
        {
            loadedOv = true;
            try
            {
                if (!File.Exists(OVR)) { MelonLogger.Warning("no overrides.json at " + OVR); return; }
                using var doc = JsonDocument.Parse(File.ReadAllText(OVR));
                var root = doc.RootElement;
                void grab(string sec, List<Dictionary<string, JsonElement>> into)
                {
                    if (!root.TryGetProperty(sec, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
                    foreach (var el in arr.EnumerateArray())
                    {
                        var d = new Dictionary<string, JsonElement>();
                        foreach (var p in el.EnumerateObject()) d[p.Name] = p.Value.Clone();
                        into.Add(d);
                    }
                }
                grab("units", ovUnits);
                grab("ammunitions", ovAmmo);
                MelonLogger.Msg("overrides loaded: " + ovUnits.Count + " units, " + ovAmmo.Count + " ammo");
            }
            catch (Exception e) { MelonLogger.Error("overrides parse: " + e.Message); }
        }

        static Units FindUnit(DataBaseService svc, Dictionary<string, JsonElement> o)
        {
            if (o.TryGetValue("id", out var idv) && idv.TryGetInt32(out int id))
            { try { var u = svc.GetUnitById(id, false); if (u != null) return u; } catch { } }
            if (o.TryGetValue("name", out var nv) && nv.ValueKind == JsonValueKind.String)
            {
                string want = nv.GetString().Trim().ToLowerInvariant();
                for (int i = 0; i <= 700; i++)
                { try { var u = svc.GetUnitById(i, false); if (u != null && u.Name != null && u.Name.Trim().ToLowerInvariant() == want) return u; } catch { } }
            }
            return null;
        }

        static int ApplyUnits(DataBaseService svc)
        {
            int changed = 0;
            foreach (var o in ovUnits)
            {
                Units u = FindUnit(svc, o);
                if (u == null) continue;
                try
                {
                    if (Has(o, "Cost")) { int c = VI(o["Cost"]); if (u.Cost != c) { u.Cost = c; u.OriginalCost = c; changed++; } }
                    if (Has(o, "Stealth")) { u.Stealth = VF(o["Stealth"]); changed++; }
                    Armors a = null; try { a = u.Armor; } catch { }
                    if (a != null)
                    {
                        if (Has(o, "HP")) { a.MaxHealthPoints = VI(o["HP"]); changed++; }
                        if (Has(o, "ArmorValue")) { a.ArmorValue = VI(o["ArmorValue"]); changed++; }
                        if (Has(o, "KinF")) { a.KinArmorFront = VI(o["KinF"]); changed++; }
                        if (Has(o, "KinS")) { a.KinArmorSides = VI(o["KinS"]); changed++; }
                        if (Has(o, "KinR")) { a.KinArmorRear = VI(o["KinR"]); changed++; }
                        if (Has(o, "KinT")) { a.KinArmorTop = VI(o["KinT"]); changed++; }
                        if (Has(o, "HeatF")) { a.HeatArmorFront = VI(o["HeatF"]); changed++; }
                        if (Has(o, "HeatS")) { a.HeatArmorSides = VI(o["HeatS"]); changed++; }
                        if (Has(o, "HeatR")) { a.HeatArmorRear = VI(o["HeatR"]); changed++; }
                        if (Has(o, "HeatT")) { a.HeatArmorTop = VI(o["HeatT"]); changed++; }
                    }
                }
                catch (Exception e) { MelonLogger.Error("unit apply: " + e.Message); }
            }
            return changed;
        }

        static int ApplyAmmo(DataBaseService svc)
        {
            int changed = 0;
            foreach (var o in ovAmmo)
            {
                Ammunitions a = null;
                if (o.TryGetValue("id", out var idv) && idv.TryGetInt32(out int id)) { try { a = svc.GetAmmunitionById(id); } catch { } }
                if (a == null) continue;
                try
                {
                    if (Has(o, "Damage")) { a.Damage = VF(o["Damage"]); changed++; }
                    if (Has(o, "PenMin")) { a.PenetrationAtMinRange = VF(o["PenMin"]); changed++; }
                    if (Has(o, "PenGround")) { a.PenetrationAtGroundRange = VF(o["PenGround"]); changed++; }
                    if (Has(o, "SupplyCost")) { a.SupplyCost = VF(o["SupplyCost"]); changed++; }
                    if (Has(o, "ResupplyTime")) { a.ResupplyTime = VF(o["ResupplyTime"]); changed++; }
                }
                catch (Exception e) { MelonLogger.Error("ammo apply: " + e.Message); }
            }
            return changed;
        }

        public override void OnUpdate()
        {
            if (++frame % 120 != 0) return;
            DataBaseService inst;
            try { inst = DataBaseService._instance; if (inst == null || !inst.IsLoaded) return; } catch { return; }

            if (!loadedOv) LoadOverrides();

            if (!exported)
            {
                exported = true;
                try { Directory.CreateDirectory(OUT); } catch { }
                Exporter.ExportAll(inst, OUT);
            }

            // apply overrides (re-apply a few times to survive scenario reloads, then rely on periodic)
            int cu = ApplyUnits(inst), ca = ApplyAmmo(inst);
            if (cu + ca > 0) { applyTicks++; if (applyTicks <= 3) MelonLogger.Msg("applied overrides: units=" + cu + " ammo=" + ca); }
        }
    }

    static class Exporter
    {
        static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
        }
        public static void ExportAll(DataBaseService inst, string OUT)
        {
            try
            {
                var sb = new StringBuilder("[\n"); bool f = true; int n = 0;
                for (int id = 0; id <= 700; id++)
                {
                    Units u = null; try { u = inst.GetUnitById(id, false); } catch { }
                    if (u == null) continue; n++;
                    int cost = 0, hp = 0, av = 0, kf = 0, ks = 0, kr = 0, kt = 0, hf = 0, hs = 0, hr = 0, ht = 0; string name = "", type = "";
                    try { cost = u.Cost; } catch { } try { name = u.Name; } catch { } try { type = u.Type.ToString(); } catch { }
                    try { Armors a = u.Armor; if (a != null) { try{hp=a.MaxHealthPoints;}catch{} try{av=a.ArmorValue;}catch{} try{kf=a.KinArmorFront;}catch{} try{ks=a.KinArmorSides;}catch{} try{kr=a.KinArmorRear;}catch{} try{kt=a.KinArmorTop;}catch{} try{hf=a.HeatArmorFront;}catch{} try{hs=a.HeatArmorSides;}catch{} try{hr=a.HeatArmorRear;}catch{} try{ht=a.HeatArmorTop;}catch{} } } catch { }
                    if (!f) sb.Append(",\n"); f = false;
                    sb.Append("  {\"id\":").Append(id).Append(",\"Name\":\"").Append(Esc(name)).Append("\",\"Type\":\"").Append(Esc(type))
                      .Append("\",\"Cost\":").Append(cost).Append(",\"HP\":").Append(hp).Append(",\"ArmorValue\":").Append(av)
                      .Append(",\"KinF\":").Append(kf).Append(",\"KinS\":").Append(ks).Append(",\"KinR\":").Append(kr).Append(",\"KinT\":").Append(kt)
                      .Append(",\"HeatF\":").Append(hf).Append(",\"HeatS\":").Append(hs).Append(",\"HeatR\":").Append(hr).Append(",\"HeatT\":").Append(ht).Append("}");
                }
                sb.Append("\n]\n"); File.WriteAllText(Path.Combine(OUT, "Units_full.json"), sb.ToString());
                MelonLogger.Msg("exported Units_full.json (" + n + ")");
            }
            catch (Exception e) { MelonLogger.Error("units export: " + e.Message); }
            try
            {
                var sb = new StringBuilder("[\n"); bool f = true; int n = 0;
                for (int id = 0; id <= 4000; id++)
                {
                    Ammunitions a = null; try { a = inst.GetAmmunitionById(id); } catch { }
                    if (a == null) continue; n++;
                    string name = ""; float dmg = 0, pmin = 0, pgnd = 0, sc = 0, rt = 0, gr = 0;
                    try { name = a.Name; } catch { } try { dmg = a.Damage; } catch { } try { pmin = a.PenetrationAtMinRange; } catch { }
                    try { pgnd = a.PenetrationAtGroundRange; } catch { } try { sc = a.SupplyCost; } catch { } try { rt = a.ResupplyTime; } catch { } try { gr = a.GroundRange; } catch { }
                    if (!f) sb.Append(",\n"); f = false;
                    sb.Append("  {\"id\":").Append(id).Append(",\"Name\":\"").Append(Esc(name)).Append("\",\"Damage\":").Append(dmg)
                      .Append(",\"PenMin\":").Append(pmin).Append(",\"PenGround\":").Append(pgnd).Append(",\"SupplyCost\":").Append(sc)
                      .Append(",\"ResupplyTime\":").Append(rt).Append(",\"GroundRange\":").Append(gr).Append("}");
                }
                sb.Append("\n]\n"); File.WriteAllText(Path.Combine(OUT, "Ammunitions_full.json"), sb.ToString());
                MelonLogger.Msg("exported Ammunitions_full.json (" + n + ")");
            }
            catch (Exception e) { MelonLogger.Error("ammo export: " + e.Message); }
        }
    }
}
