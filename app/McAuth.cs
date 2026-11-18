using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Peach
{
    internal class DeviceCode { public string Code, UserCode, VerificationUri; public int ExpiresIn = 900, Interval = 5; }
    internal class MsTokens { public string Access, Refresh; }
    internal class McSession { public string AccessToken, Name, Uuid; }

    // Microsoft login for Minecraft (device code flow): you sign in on microsoft.com, Peach only receives tokens.
    // Needs a Client ID of your own Azure app (public client). The refresh token is stored with Windows DPAPI.
    internal static class McAuth
    {
        const string Scope = "XboxLive.signin offline_access";
        const string Tenant = "https://login.microsoftonline.com/consumers/oauth2/v2.0";

        public static bool ValidClientId(string id) { return id != null && Regex.IsMatch(id.Trim(), @"^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$"); }

        static string Form(params string[] kv)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i + 1 < kv.Length; i += 2) { if (sb.Length > 0) sb.Append('&'); sb.Append(Uri.EscapeDataString(kv[i])).Append('=').Append(Uri.EscapeDataString(kv[i + 1])); }
            return sb.ToString();
        }

        static Dictionary<string, object> Obj(string json) { try { return Web.Json().Deserialize<Dictionary<string, object>>(json); } catch { return null; } }
        static string S(Dictionary<string, object> d, string k) { return d != null && d.ContainsKey(k) && d[k] != null ? d[k].ToString() : null; }

        // ---- step 1: ask for a code the user types on microsoft.com/link ----
        public static DeviceCode StartDeviceCode(string clientId)
        {
            int st;
            string r = Web.Send("POST", Tenant + "/devicecode", Form("client_id", clientId.Trim(), "scope", Scope), "application/x-www-form-urlencoded", null, 20000, out st);
            DeviceCode dc = ParseDeviceCode(r);
            if (dc == null) throw new InvalidOperationException(ErrorText(r) ?? "Microsoft hat keinen Anmeldecode geliefert (stimmt die Client-ID?).");
            return dc;
        }

        public static DeviceCode ParseDeviceCode(string json)
        {
            Dictionary<string, object> d = Obj(json);
            if (d == null || S(d, "device_code") == null || S(d, "user_code") == null) return null;
            DeviceCode dc = new DeviceCode { Code = S(d, "device_code"), UserCode = S(d, "user_code"), VerificationUri = S(d, "verification_uri") ?? "https://www.microsoft.com/link" };
            int n; if (int.TryParse(S(d, "expires_in"), out n)) dc.ExpiresIn = n;
            if (int.TryParse(S(d, "interval"), out n)) dc.Interval = Math.Max(2, n);
            if (!dc.VerificationUri.StartsWith("https://")) dc.VerificationUri = "https://www.microsoft.com/link";
            return dc;
        }

        // ---- step 2: wait until the user has signed in. Returns null while still pending; throws when declined / expired ----
        public static MsTokens PollToken(string clientId, string deviceCode)
        {
            int st;
            string r = Web.Send("POST", Tenant + "/token", Form("grant_type", "urn:ietf:params:oauth:grant-type:device_code", "client_id", clientId.Trim(), "device_code", deviceCode), "application/x-www-form-urlencoded", null, 20000, out st);
            return ParseToken(r);
        }

        public static MsTokens Refresh(string clientId, string refreshToken)
        {
            int st;
            string r = Web.Send("POST", Tenant + "/token", Form("grant_type", "refresh_token", "client_id", clientId.Trim(), "refresh_token", refreshToken, "scope", Scope), "application/x-www-form-urlencoded", null, 20000, out st);
            MsTokens t = ParseToken(r);
            if (t == null) throw new InvalidOperationException("Die Anmeldung ist abgelaufen. Bitte neu anmelden.");
            return t;
        }

        // null = "authorization_pending" / "slow_down"; throws for real errors
        public static MsTokens ParseToken(string json)
        {
            Dictionary<string, object> d = Obj(json);
            if (d == null) throw new InvalidOperationException("Unerwartete Antwort von Microsoft.");
            if (S(d, "access_token") != null) return new MsTokens { Access = S(d, "access_token"), Refresh = S(d, "refresh_token") };
            string err = S(d, "error");
            if (err == "authorization_pending" || err == "slow_down") return null;
            if (err == "authorization_declined") throw new InvalidOperationException("Die Anmeldung wurde abgelehnt.");
            if (err == "expired_token") throw new InvalidOperationException("Der Code ist abgelaufen. Bitte noch einmal auf „Anmelden“ klicken.");
            throw new InvalidOperationException(ErrorText(json) ?? "Anmeldung fehlgeschlagen.");
        }

        static string ErrorText(string json)
        {
            Dictionary<string, object> d = Obj(json);
            string desc = S(d, "error_description");
            if (desc == null) return S(d, "error");
            int nl = desc.IndexOf('\r'); if (nl > 0) desc = desc.Substring(0, nl);
            return desc;
        }

        // ---- step 3: Xbox Live -> XSTS -> Minecraft ----
        public static McSession ToMinecraft(string msAccessToken)
        {
            int st;
            Dictionary<string, string> h = new Dictionary<string, string> { { "x-xbl-contract-version", "1" } };
            string xbl = Web.Send("POST", "https://user.auth.xboxlive.com/user/authenticate",
                "{\"Properties\":{\"AuthMethod\":\"RPS\",\"SiteName\":\"user.auth.xboxlive.com\",\"RpsTicket\":\"d=" + Json(msAccessToken) + "\"},\"RelyingParty\":\"http://auth.xboxlive.com\",\"TokenType\":\"JWT\"}", "application/json", h, 20000, out st);
            string xblToken, uhs;
            if (st != 200 || !ParseXbox(xbl, out xblToken, out uhs)) throw new InvalidOperationException("Xbox-Anmeldung fehlgeschlagen (HTTP " + st + ").");
            string xsts = Web.Send("POST", "https://xsts.auth.xboxlive.com/xsts/authorize",
                "{\"Properties\":{\"SandboxId\":\"RETAIL\",\"UserTokens\":[\"" + Json(xblToken) + "\"]},\"RelyingParty\":\"rp://api.minecraftservices.com/\",\"TokenType\":\"JWT\"}", "application/json", h, 20000, out st);
            string xstsToken, uhs2;
            if (st != 200) throw new InvalidOperationException(XstsError(xsts));
            if (!ParseXbox(xsts, out xstsToken, out uhs2)) throw new InvalidOperationException("Xbox-Antwort unverständlich.");
            string mc = Web.Send("POST", "https://api.minecraftservices.com/authentication/login_with_xbox",
                "{\"identityToken\":\"XBL3.0 x=" + Json(uhs2) + ";" + Json(xstsToken) + "\"}", "application/json", null, 20000, out st);
            Dictionary<string, object> m = Obj(mc);
            if (st != 200 || S(m, "access_token") == null) throw new InvalidOperationException("Minecraft-Anmeldung fehlgeschlagen (HTTP " + st + "). Hat dein Azure-App-Eintrag die Freigabe von Mojang?");
            string token = S(m, "access_token");
            string prof = Web.Send("GET", "https://api.minecraftservices.com/minecraft/profile", null, null, new Dictionary<string, string> { { "Authorization", "Bearer " + token } }, 20000, out st);
            if (st == 404) throw new InvalidOperationException("Zu diesem Microsoft-Konto gehört kein Minecraft Java Edition.");
            McSession s = ParseProfile(prof);
            if (st != 200 || s == null) throw new InvalidOperationException("Minecraft-Profil konnte nicht gelesen werden (HTTP " + st + ").");
            s.AccessToken = token;
            return s;
        }

        static string Json(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); }

        public static bool ParseXbox(string json, out string token, out string uhs)
        {
            token = null; uhs = null;
            Dictionary<string, object> d = Obj(json);
            token = S(d, "Token");
            Dictionary<string, object> claims = d != null && d.ContainsKey("DisplayClaims") ? d["DisplayClaims"] as Dictionary<string, object> : null;
            object[] xui = claims != null && claims.ContainsKey("xui") ? Modrinth.ToArray(claims["xui"]) : null;
            if (xui != null && xui.Length > 0) uhs = S(xui[0] as Dictionary<string, object>, "uhs");
            return token != null && uhs != null;
        }

        public static string XstsError(string json)
        {
            Dictionary<string, object> d = Obj(json);
            string x = S(d, "XErr");
            switch (x)
            {
                case "2148916233": return "Zu diesem Microsoft-Konto gibt es kein Xbox-Profil. Erstelle eins auf xbox.com und versuche es noch einmal.";
                case "2148916235": return "Xbox Live ist in deinem Land nicht verfügbar.";
                case "2148916236": case "2148916237": return "Für dieses Konto ist eine Altersprüfung nötig (xbox.com).";
                case "2148916238": return "Dieses Konto gehört zu einem Kind und muss von den Eltern in einer Familiengruppe freigegeben werden.";
                default: return "Xbox hat die Anmeldung abgelehnt" + (x != null ? " (Fehler " + x + ")." : ".");
            }
        }

        public static McSession ParseProfile(string json)
        {
            Dictionary<string, object> d = Obj(json);
            string id = S(d, "id"), name = S(d, "name");
            if (id == null || name == null || !Regex.IsMatch(id, "^[0-9a-fA-F]{32}$") || !Regex.IsMatch(name, @"^\w{1,16}$")) return null;
            return new McSession { Name = name, Uuid = Regex.Replace(id.ToLowerInvariant(), "^(.{8})(.{4})(.{4})(.{4})(.{12})$", "$1-$2-$3-$4-$5") };
        }

        // ---- token storage: Windows DPAPI, readable only by this Windows user ----
        public static string Protect(string secret)
        {
            if (string.IsNullOrEmpty(secret)) return "";
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes("peach-mc"), DataProtectionScope.CurrentUser));
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Encoding.UTF8.GetBytes("peach-mc"), DataProtectionScope.CurrentUser)); }
            catch { return null; }
        }

        // Full refresh: stored refresh token -> new Minecraft session. Saves the new refresh token.
        public static McSession SignInFromStored(Config cfg)
        {
            MinecraftConfig m = cfg.Minecraft;
            string rt = Unprotect(m.RefreshToken);
            if (rt == null || !ValidClientId(m.ClientId)) throw new InvalidOperationException("Nicht angemeldet.");
            MsTokens t = Refresh(m.ClientId, rt);
            McSession s = ToMinecraft(t.Access);
            if (!string.IsNullOrEmpty(t.Refresh)) { m.RefreshToken = Protect(t.Refresh); }
            m.AccountName = s.Name; m.AccountUuid = s.Uuid; cfg.Save();
            return s;
        }
    }
}
