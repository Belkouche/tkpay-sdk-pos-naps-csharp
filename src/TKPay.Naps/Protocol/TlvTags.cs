namespace TKPay.Naps;

/// <summary>
/// NAPS Pay M2M TLV tag codes (3-digit numeric strings).
/// </summary>
internal static class TlvTags
{
    // ---- Field tags ----
    internal const string Tm   = "001"; // Message Type (3 chars)
    internal const string Mt   = "002"; // Amount in centimes (12 chars)
    internal const string Ncai = "003"; // Register(2) + Cashier(5) = 7 chars
    internal const string Ns   = "004"; // Sequence Number (6 chars)
    internal const string Nsa  = "005"; // Cancellation Sequence Number (6 chars)
    internal const string Nhc  = "006"; // Hostess Number (2 chars)
    internal const string Ncar = "007"; // Card Number — masked immediately on parse (16 chars)
    internal const string Stan = "008"; // System Trace Audit Number (6 chars)
    internal const string Na   = "009"; // Authorization Number (6 chars)
    internal const string Dp   = "010"; // Printable Data / Receipt (up to 3500 chars)
    internal const string Cb   = "011"; // Barcode (100 chars)
    internal const string DE   = "012"; // Currency Code (3 chars)
    internal const string CR   = "013"; // Response Code (3 chars)
    internal const string Da   = "014"; // Date DDMMYYYY (8 chars)
    internal const string He   = "015"; // Time HHMMSS (6 chars)
    internal const string Nprt = "016"; // Cardholder Name (48 chars)
    internal const string Daex = "017"; // Expiration Date YYMM (4 chars)
    internal const string Datr = "018"; // Transaction Date DDMMYYYY (8 chars)
    internal const string Hetr = "019"; // Transaction Time HHMMSS (6 chars)
    internal const string Tide = "020"; // Ticket Type (2 chars)
    internal const string Typa = "021"; // Transaction Type (1 char)
    internal const string Re   = "022"; // Receipt Data (256 chars)
    internal const string Recb = "023"; // Receipt Copy (25 chars)
    internal const string Requ = "024"; // Request Type (2 chars)
    internal const string Rese = "025"; // Response Message (25 chars)
    internal const string Reco = "026"; // Receipt Confirmation (25 chars)
    internal const string Rera = "027"; // Response Reason (2 chars)
    internal const string Mdlc = "028"; // Model Code (3 chars)
    internal const string Em   = "040"; // Entry Mode (3 chars)

    // ---- Receipt sub-tags (within DP/010) ----
    internal const string ReceiptLineNumber = "030"; // 2 chars
    internal const string ReceiptFormat    = "031";  // S=Simple, G=Gras/Bold
    internal const string ReceiptAlignment = "032";  // C=Center, D=Droite/Right, G=Gauche/Left
    internal const string ReceiptContent   = "033";  // Variable length

    // ---- Message types ----
    internal const string MsgPaymentRequest       = "001";
    internal const string MsgPaymentResponse      = "101";
    internal const string MsgConfirmationRequest  = "002";
    internal const string MsgConfirmationResponse = "102";
    internal const string MsgSettlementRequest    = "010";
    internal const string MsgSettlementResponse   = "110";

    // ---- Currency ----
    internal const string CurrencyMad = "504"; // Moroccan Dirham

    // ---- Response codes ----
    internal const string RcApproved = "000";

    /// <summary>Human-readable name for debugging.</summary>
    internal static string Name(string tag) => tag switch
    {
        Tm   => "Message Type",
        Mt   => "Amount",
        Ncai => "Terminal Number",
        Ns   => "Sequence Number",
        Nsa  => "Cancellation Sequence",
        Nhc  => "Hostess Number",
        Ncar => "Card Number",
        Stan => "STAN",
        Na   => "Auth Number",
        Dp   => "Printable Data",
        Cb   => "Barcode",
        DE   => "Currency",
        CR   => "Response Code",
        Da   => "Date",
        He   => "Time",
        Nprt => "Cardholder Name",
        Daex => "Expiration Date",
        Datr => "Transaction Date",
        Hetr => "Transaction Time",
        Tide => "Ticket Type",
        Typa => "Transaction Type",
        Re   => "Receipt Data",
        Recb => "Receipt Copy",
        Requ => "Request Type",
        Rese => "Response Message",
        Reco => "Receipt Confirmation",
        Rera => "Response Reason",
        Mdlc => "Model Code",
        Em   => "Entry Mode",
        _    => $"Unknown Tag {tag}"
    };
}
