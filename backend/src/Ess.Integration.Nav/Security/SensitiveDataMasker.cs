using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ess.Integration.Nav.Configuration;
using Microsoft.Extensions.Options;

namespace Ess.Integration.Nav.Security;

/// <summary>
/// Masks secrets and personal data in integration request/response logs (spec §3.4, Kenyan DPA data minimisation).
/// Works on JSON and on SOAP/XML bodies. Payroll amounts are masked too: logs are not a payroll channel.
/// </summary>
public sealed partial class SensitiveDataMasker
{
    public const string Mask = "***";

    private static readonly string[] BuiltIn =
    {
        "password", "passwd", "pwd", "secret", "client_secret", "access_token", "refresh_token", "token", "authorization",
        "apikey", "api_key", "webservicekey",
        // personal identifiers (Kenya): ID/passport, KRA PIN, NSSF, NHIF/SHIF/SHA numbers
        "nationalid", "national_id", "idnumber", "id_number", "passport", "passportno", "kra_pin", "krapin", "pin", "pinno",
        "nssfno", "nssf_no", "nhifno", "nhif_no", "shifno", "shif_no", "shano",
        // bank
        "bankaccount", "bank_account", "bankaccountno", "accountno", "account_no", "iban", "swift",
        // payroll amounts
        "basicpay", "basic_pay", "grosspay", "gross_pay", "netpay", "net_pay", "amount", "salary", "paye", "ytd", "yeartodate",
        // personal contact & health
        "dateofbirth", "birthdate", "birth_date", "medical", "diagnosis"
    };

    private readonly HashSet<string> _fields;
    private readonly int _maxChars;

    public SensitiveDataMasker(IOptions<NavEngineOptions> options)
    {
        _fields = new HashSet<string>(BuiltIn.Concat(options.Value.AdditionalSensitiveFields).Select(Normalize));
        _maxChars = options.Value.LogBodyMaxChars;
    }

    public bool IsSensitive(string fieldName) => _fields.Contains(Normalize(fieldName));

    public string? MaskBody(string? body)
    {
        if (string.IsNullOrEmpty(body)) return body;
        var trimmed = body.TrimStart();
        string masked;
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            masked = MaskJson(body);
        else if (trimmed.StartsWith('<'))
            masked = MaskXml(body);
        else
            masked = BearerRegex().Replace(body, "Bearer " + Mask);
        return masked.Length > _maxChars ? masked[.._maxChars] + "…[truncated]" : masked;
    }

    public string MaskUrl(string url) =>
        QuerySecretRegex().Replace(url, m => m.Groups[1].Value + "=" + Mask);

    private string MaskJson(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            Walk(node);
            return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? json;
        }
        catch (JsonException)
        {
            return MaskXml(json); // fall back to regex masking
        }
    }

    private void Walk(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    if (IsSensitive(key) && obj[key] is not JsonObject and not JsonArray) obj[key] = Mask;
                    else Walk(obj[key]);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr) Walk(item);
                break;
        }
    }

    private string MaskXml(string xml) =>
        XmlElementRegex().Replace(xml, m =>
            IsSensitive(m.Groups["name"].Value) ? $"<{m.Groups["tag"].Value}>{Mask}</{m.Groups["close"].Value}>" : m.Value);

    private static string Normalize(string s) => s.Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();

    // <ns:Field attr="..">value</ns:Field>  (simple leaf elements only)
    [GeneratedRegex(@"<(?<tag>(?:[\w-]+:)?(?<name>[\w-]+)(?:\s[^>]*)?)>(?<val>[^<]*)</(?<close>(?:[\w-]+:)?\k<name>)>", RegexOptions.CultureInvariant)]
    private static partial Regex XmlElementRegex();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-\._~\+/]+=*", RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"(?i)\b(password|secret|token|key|sig|code)=[^&]*")]
    private static partial Regex QuerySecretRegex();
}
