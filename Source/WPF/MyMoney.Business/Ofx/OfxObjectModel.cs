using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

// This file contains the OFX object model that implements the Open Financial Exchange spec version 2.03.
namespace Walkabout.Ofx
{
    // OfxErrorCode lives in the sibling OfxErrorCode.cs (same namespace) - see that file.

    public class OFX
    {
        public OFX() { }

        [XmlElement("SIGNONMSGSRSV1")]
        public SignOnResponseMessageSet SignOnMessageResponse { get; set; }

        [XmlElement("SIGNUPMSGSRSV1")]
        public SignUpResponseMessageSet SignUpMessageResponse { get; set; }

        [XmlElement("PROFMSGSRSV1")]
        public ProfileResponseMessageSet ProfileMessageSet { get; set; }

        public static OFX Deserialize(XDocument doc)
        {
            try
            {
                OFX ofx;
                XmlSerializer s = new XmlSerializer(typeof(OFX));
                using (XmlReader r = XmlReader.Create(new StringReader(doc.ToString())))
                {
                    ofx = (OFX)s.Deserialize(r);
                }
                return ofx;
            }
            catch
            {
                throw new OfxException("Error parsing OFX response", "Error", doc.ToString(), null);
            }
        }

    }

    public class MfaChallengeTransaction : TransactionWrapper
    {
        [XmlArrayItem("MFACHALLENGE")]
        [XmlArray("MFACHALLENGERS")]
        public List<MfaChallenge> Challenges { get; set; }
    }


    public class MfaChallenge
    {
        /// <summary>
        /// Identifier for the challenge question. It should be unique for this challenge question but not unique for the user, session, etc. A-32. 
        /// </summary>
        [XmlElement("MFAPHRASEID")]
        public string PhraseId { get; set; }

        /// <summary>
        /// The textual challenge question. This should be as appropriate as possible for display to the user. A-64
        /// </summary>
        [XmlElement("MFAPHRASELABEL")]
        public string PhraseLabel { get; set; }
    }

    public class ProfileResponseMessageSet
    {
        public ProfileResponseMessageSet() { }

        [XmlElement("PROFTRNRS")]
        public ProfileMessageResponse ProfileMessageResponse { get; set; }
    }


    public class SignOnResponseMessageSet
    {
        public SignOnResponseMessageSet() { }

        [XmlElement("SONRS")]
        public SignOnResponse SignOnResponse { get; set; }

        [XmlElement("PINCHTRNRS")]
        public PinChangeResponseTransaction PinChangeResponseTransaction { get; set; }

        [XmlElement("MFACHALLENGETRNRS")]
        public MfaChallengeTransaction MfaChallengeTransaction { get; set; }
    }

    public class OfxSignOnInfoList
    {
        [XmlElement("SIGNONINFO")]
        public OfxSignOnInfo[] OfxSignOnInfo { get; set; }
    }

    public class OfxSignOnInfo
    {
        public OfxSignOnInfo() { }

        /// <summary>
        /// Identifies this realm
        /// </summary>
        [XmlElement("SIGNONREALMN")]
        public string SignOnRealm { get; set; }

        /// <summary>
        /// Minimum number of password characters, N-2
        /// </summary>
        [XmlElement("MIN")]
        public int MinimumLength { get; set; }

        /// <summary>
        /// Maximum number of password characters, N-2
        /// </summary>
        [XmlElement("MAX")]
        public int MaximumLength { get; set; }

        /// <summary>
        /// Type of characters allowed in password
        /// ALPHAONLY - Password may not contain numeric characters. The server would allow “abbc”, but not “1223” or “a122”.
        /// NUMERICONLY - Password may not contain alphabetic characters. The server would allow “1223”, but not “abbc” or “a122”.
        /// ALPHAORNUMERIC - Password may contain alphabetic or numeric characters (or both). The server would allow “abbc”, “1223”, or “a122”.
        /// ALPHAANDNUMERIC - Password must contain both alphabetic and numeric characters. The server would allow “a122”, but not “abbc” or “1223”.
        /// </summary>
        [XmlElement("CHARTYPE")]
        public string CharType { get; set; }

        /// <summary>
        /// Y if password is case-sensitive, Boolean
        /// </summary>
        [XmlElement("CASESEN")]
        public string CaseSensitive { get; set; }

        /// <summary>
        /// Y if special characters are allowed over and above those characters allowed by CHARTYPE and SPACES, Boolean
        /// </summary>
        [XmlElement("SPECIAL")]
        public string SpecialCharsAllowed { get; set; }

        /// <summary>
        /// Y if spaces are allowed over and above those characters allowed by CHARTYPE and SPECIAL, Boolean
        /// </summary>
        [XmlElement("SPACES")]
        public string SpacesAllowed { get; set; }

        /// <summary>
        /// Y if server supports <PINCHRQ> (PIN change requests), Boolean
        /// </summary>
        [XmlElement("PINCH")]
        public string PinChangeAllowed { get; set; }

        /// <summary>
        /// Y if server requires clients to change USERPASS as part of first signon. However, if MFACHALLENGEFIRST is also Y, this pin change request should be sent immediately after the session containing MFACHALLENGE authentication. Boolean
        /// </summary>
        [XmlElement("CHGPINFIRST")]
        public string PasswordChangeRequired { get; set; }

        /// <summary>
        /// Text prompt for user credential. If it is present, a third credential (USERCRED1) is required in addition to USERID and USERPASS. A-64
        /// </summary>
        [XmlElement("USERCRED1LABEL")]
        public string UserCredentialLabel1 { get; set; }

        /// <summary>
        /// Text prompt for user credential. If it is present, a fourth credential (USERCRED2) is required in addition to USERID, USERPASS and USERCRED1. If present, USERCRED1LABEL must also be present. A-64
        /// </summary>
        [XmlElement("USERCRED2LABEL")]
        public string UserCredentialLabel2 { get; set; }

        /// <summary>
        /// Y if CLIENTUID is required, Boolean
        /// </summary>
        [XmlElement("CLIENTUIDREQ")]
        public string ClientUidRequired { get; set; }

        /// <summary>
        /// Y if server requires clients to send AUTHTOKEN as part of the first signon, Boolean
        /// </summary>
        [XmlElement("AUTHTOKENFIRST")]
        public string AuthTokenRequired { get; set; }

        /// <summary>
        /// Text label for the AUTHTOKEN. Required if server supports AUTHTOKEN, A-64
        /// </summary>
        [XmlElement("AUTHTOKENLABEL")]
        public string AuthTokenLabel { get; set; }

        /// <summary>
        /// URL where AUTHTOKEN information is provided by the institution operating the OFX server. Required if server supports AUTHTOKEN, A-255
        /// </summary>
        [XmlElement("AUTHTOKENINFOURL")]
        public string AuthTokenInfoUrl { get; set; }

        /// <summary>
        /// Y if the server supports MFACHALLENGE functionality, Boolean
        /// </summary>
        [XmlElement("MFACHALLENGESUPT")]
        public string MFAChallengeSupported { get; set; }

        /// <summary>
        /// Y if the client is required to send MFACHALLENGERQ as part of the first signon, before sending any other requests, Boolean
        /// </summary>
        [XmlElement("MFACHALLENGEFIRST")]
        public string MFAChallengeRequired { get; set; }

    }

    public class ProfileMessageResponse : TransactionWrapper
    {
        [XmlElement("PROFRS")]
        public ProfileResponse OfxProfile { get; set; }
    }

    public class MessageSetCore
    {
        /// <summary>
        /// Version number of the message set, (for example, <VER>1 for version 1 of the message set), N-5
        /// </summary>
        [XmlElement("VER")]
        public string Version { get; set; }

        /// <summary>
        /// URL where messages in this set are to be sent, URL
        /// </summary>
        [XmlElement("URL")]
        public string Url { get; set; }

        /// <summary>
        /// Security level required for this message set;
        /// </summary>
        [XmlElement("OFXSEC")]
        public string SecurityLevel { get; set; }

        /// <summary>
        /// Y if transport-level security must be used, N if not used;
        /// </summary>
        [XmlElement("TRANSPSEC")]
        public string TransportLevelSecurity { get; set; }

        /// <summary>
        /// Signon realm to use with this message set, A-32
        /// </summary>
        [XmlElement("SIGNONREALM")]
        public string SignOnRealm { get; set; }

        /// <summary>
        /// Language supported, language.
        /// </summary>
        [XmlElement("LANGUAGE")]
        public string[] Language { get; set; }

        /// <summary>
        /// FULL for full synchronization capability
        /// LITE for lite synchronization capability
        /// </summary>
        [XmlElement("SYNCMODE")]
        public string SyncMode { get; set; }

        /// <summary>
        /// Y if server supports REFRESH within synchronizations
        /// </summary>
        [XmlElement("REFRESHSUPT")]
        public string RefreshSupported { get; set; }

        /// <summary>
        /// Y if server supports file-based error recovery
        /// </summary>
        [XmlElement("RESPFILEER")]
        public string FileBasedErrorRecoverySupported { get; set; }

        /// <summary>
        /// Service provider name
        /// </summary>
        [XmlElement("SPNAME")]
        public string SPNAME { get; set; }

        [XmlElement("INTU.TIMEOUT")]
        public string Timeout { get; set; }
    }

    public class SignOnMessageSetV1
    {
        [XmlElement("MSGSETCORE")]
        public MessageSetCore MessageSetCore { get; set; }
    }

    public class SignOnMessageSet
    {
        [XmlElement("SIGNONMSGSETV1")]
        public SignOnMessageSetV1 SignOnMessageSetV1 { get; set; }
    }

    public class ClientEnrollInfo
    {
        /// <summary>
        /// Y if account number is required as part of enrollment
        /// </summary>
        [XmlElement("ACCTREQUIRED")]
        public string AccountNumberRequired { get; set; }
    }

    public class OtherEnrollInfo
    {
        /// <summary>
        /// Message to consumer about what to do next (for example, a phone number)
        /// </summary>
        [XmlElement("MESSAGE")]
        public string Message { get; set; }
    }

    public class WebEnrollInfo
    {
        /// <summary>
        /// URL to start enrollment process
        /// </summary>
        [XmlElement("URL")]
        public string Url { get; set; }
    }

    // this is not the same as SignUpMessageSet
    public class SignUpMessageSet
    {
        [XmlElement("SIGNUPMSGSETV1")]
        public SignUpMessageSetV1 SignUpMessageSetV1 { get; set; }
    }

    // this is not the same as SignUpMessageSet
    public class SignUpMessageSetV1
    {
        [XmlElement("MSGSETCORE")]
        public MessageSetCore MessageSetCore { get; set; }

        /// <summary>
        /// Client-based enrollment supported
        /// </summary>
        [XmlElement("CLIENTENROLL")]
        public ClientEnrollInfo ClientEnrollInfo { get; set; }

        /// <summary>
        /// Some other enrollment process
        /// </summary>
        [XmlElement("OTHERENROLL")]
        public OtherEnrollInfo OtherEnrollInfo { get; set; }

        /// <summary>
        /// Web-based enrollment supported
        /// </summary>
        [XmlElement("WEBENROLL")]
        public WebEnrollInfo WebEnrollInfo { get; set; }

        /// <summary>
        /// Y if server supports client-based user information changes
        /// </summary>
        [XmlElement("CHGUSERINFO")]
        public string ChangeUserInfo { get; set; }

        /// <summary>
        /// Y if server can provide information on accounts with SVCSTATUS available, 
        /// N means client should expect to ask user for specific account information
        /// </summary>
        [XmlElement("AVAILACCTS")]
        public string AvailableAccounts { get; set; }

        /// <summary>
        /// Y if server allows clients to make service activation requests
        /// </summary>
        [XmlElement("CLIENTACTREQ")]
        public string ClientActivationAllowed { get; set; }
    }


    // this is not the same as SignUpMessageSet
    public class CreditCardMessageV1
    {
        [XmlElement("MSGSETCORE")]
        public MessageSetCore MessageSetCore { get; set; }

        /// <summary>
        /// Closing statement information available
        /// </summary>
        [XmlElement("CLOSINGAVAIL")]
        public string ClosingStatementInformationAvailable { get; set; }
    }

    // this is not the same as SignUpMessageSet
    public class CreditCardMessageSet
    {
        [XmlElement("CREDITCARDMSGSETV1")]
        public CreditCardMessageV1 CreditCardMessageV1 { get; set; }
    }

    public class ProfileMessageSetV1
    {
        [XmlElement("MSGSETCORE")]
        public MessageSetCore MessageSetCore { get; set; }
    }

    public class ProfileMessageSet
    {
        [XmlElement("PROFMSGSETV1")]
        public ProfileMessageSetV1 ProfileMessageSetV1 { get; set; }
    }

    public class EmailProfile
    {
        /// <summary>
        /// Supports generalized banking e-mail
        /// </summary>
        [XmlElement("CANEMAIL")]
        public string CanEmail { get; set; }

        /// <summary>
        /// Supports notification (of any kind)
        /// </summary>
        [XmlElement("CANNOTIFY")]
        public string CanNotify { get; set; }
    }

    public class BankMessageSetV1
    {
        [XmlElement("MSGSETCORE")]
        public MessageSetCore MessageSetCore { get; set; }

        [XmlElement("EMAILPROF")]
        public EmailProfile EmailProfile { get; set; }

        /// <summary>
        /// Closing statement information available
        /// </summary>
        [XmlElement("CLOSINGAVAIL")]
        public string ClosingStatementInformationAvailable { get; set; }
    }

    public class BankMessageSet
    {
        [XmlElement("BANKMSGSETV1")]
        public BankMessageSetV1 BankMessageSetV1 { get; set; }
    }

    public class MessageSetList
    {
        [XmlElement("SIGNONMSGSET")]
        public SignOnMessageSet SignOnMessageSet { get; set; }

        [XmlElement("SIGNUPMSGSET")]
        public SignUpMessageSet SignUpMessageSet { get; set; }

        [XmlElement("CREDITCARDMSGSET")]
        public CreditCardMessageSet CreditCardMessageSet { get; set; }

        [XmlElement("BANKMSGSET")]
        public BankMessageSet BankMessageSet { get; set; }

        [XmlElement("PROFMSGSET")]
        public ProfileMessageSet ProfileMessageSet { get; set; }
    }

    public class ProfileResponse
    {
        public ProfileResponse() { }

        [XmlElement("MSGSETLIST")]
        public MessageSetList MessageSetList { get; set; }

        [XmlElement("SIGNONINFOLIST")]
        public OfxSignOnInfoList OfxSignOnInfoList { get; set; }

        [XmlElement("DTPROFUP")]
        public string ProfileUpdateDate { get; set; }

        [XmlElement("FINAME")]
        public string FinancialInstitutionName { get; set; }

        [XmlElement("ADDR1")]
        public string Address1 { get; set; }

        [XmlElement("ADDR2")]
        public string Address2 { get; set; }

        [XmlElement("ADDR3")]
        public string Address3 { get; set; }

        [XmlElement("CITY")]
        public string City { get; set; }

        [XmlElement("STATE")]
        public string State { get; set; }

        [XmlElement("POSTALCODE")]
        public string PostalCode { get; set; }

        [XmlElement("COUNTRY")]
        public string Country { get; set; }

        /// <summary>
        /// Customer service telephone number,
        /// </summary>
        [XmlElement("CSPHONE")]
        public string CustomerServicePhone { get; set; }

        /// <summary>
        /// Technical support telephone number,
        /// </summary>
        [XmlElement("TSPHONE")]
        public string TechnicalSupportPhone { get; set; }

        /// <summary>
        /// Fax number
        /// </summary>
        [XmlElement("FAXPHONE")]
        public string FaxNumber { get; set; }

        /// <summary>
        /// URL for general information about FI
        /// </summary>
        [XmlElement("URL")]
        public string CompanyUrl { get; set; }

        /// <summary>
        /// E-mail address for FI
        /// </summary>
        [XmlElement("EMAIL")]
        public string Email { get; set; }

        /// <summary>
        /// Intuit extension
        /// </summary>
        [XmlElement("INTU.BROKERID")]
        public IntuitBrokerId IntuitBrokerId { get; set; }

    }


    //<INTU.BROKERID>
    //  dstsystems.com<ADDR1>816 Broadway</ADDR1><CITY>Kansas City</CITY><STATE>MO</STATE><POSTALCODE>64105</POSTALCODE><COUNTRY>USA</COUNTRY>
    //</INTU.BROKERID>
    public class IntuitBrokerId
    {
        [XmlText]
        public string Name { get; set; }

        [XmlElement("ADDR1")]
        public string Address1 { get; set; }

        [XmlElement("CITY")]
        public string City { get; set; }

        [XmlElement("STATE")]
        public string State { get; set; }

        [XmlElement("POSTALCODE")]
        public string PostalCode { get; set; }

        [XmlElement("COUNTRY")]
        public string Country { get; set; }
    }

    public class SignOnResponse
    {
        public SignOnResponse() { }

        [XmlElement("STATUS")]
        public OfxStatus OfxStatus { get; set; }

        [XmlElement("DTSERVER")]
        public string ServerDate { get; set; }

        [XmlElement("LANGUAGE")]
        public string Language { get; set; }

        [XmlElement("DTPROFUP")]
        public string ProfileUpdateDate { get; set; }

        [XmlElement("DTACCTUP")]
        public string AccountUpdateDate { get; set; }

        [XmlElement("USERKEY")]
        public string UserKey { get; set; }

        [XmlElement("TSKEYEXPIRE")]
        public string UserKeyExpireDate { get; set; }

        [XmlElement("FI")]
        public FinancialInstitution FinancialInstitution { get; set; }

        [XmlElement("SESSCOOKIE")]
        public string SessionCookie { get; set; }

        [XmlElement("ACCESSKEY")]
        public string AccessKey { get; set; }
    }

    public class FinancialInstitution
    {
        public FinancialInstitution() { }
        [XmlElement("ORG")]
        public string Organization { get; set; }

        [XmlElement("FID")]
        public string FID { get; set; }
    }

    public class OfxStatus
    {
        public OfxStatus() { }

        [XmlElement("CODE")]
        public int Code { get; set; }

        [XmlElement("SEVERITY")]
        public string Severity { get; set; }

        [XmlElement("MESSAGE")]
        public string Message { get; set; }
    }


    public class SignUpResponseMessageSet
    {
        public SignUpResponseMessageSet() { }

        [XmlElement("ACCTINFOTRNRS")]
        public AccountInfoSet AccountInfoSet { get; set; }
    }

    public class TransactionWrapper
    {
        [XmlElement("TRANUID")]
        public string TransactionId { get; set; }

        [XmlElement("STATUS")]
        public OfxStatus OfxStatus { get; set; }

        [XmlElement("CLTCOOKIE")]
        public string Cookie { get; set; }
    }

    public class AccountInfoSet : TransactionWrapper
    {
        public AccountInfoSet() { }

        [XmlArrayItem("ACCTINFO")]
        [XmlArray("ACCTINFORS")]
        public List<AccountInfoResponse> Accounts { get; set; }
    }

    public class AccountInfoResponse
    {
        public AccountInfoResponse() { }

        [XmlElement("DESC")]
        public string Description { get; set; }

        [XmlElement("BANKACCTINFO")]
        public BankAccountInfo BankAccountInfo { get; set; }

        [XmlElement("CCACCTINFO")]
        public CreditCardAccountInfo CreditCardAccountInfo { get; set; }

        [XmlElement("INVACCTINFO")]
        public InvestmentAccountInfo InvAccountInfo { get; set; }

    }

    public class BankAccountInfo
    {
        public BankAccountInfo() { }

        [XmlElement("BANKACCTFROM")]
        public BankAccountFrom BankAccountFrom { get; set; }

        [XmlElement("SUPTXDL")]
        public string SupportsDownload { get; set; }

        [XmlElement("XFERSRC")]
        public string TransferSourceEnabled { get; set; }

        [XmlElement("XFERDEST")]
        public string TransferDestinationEnabled { get; set; }

        [XmlElement("SVCSTATUS")]
        public string ActivationStatus { get; set; }
    }

    public class BankAccountFrom
    {
        public BankAccountFrom() { }

        [XmlElement("ACCTID")]
        public string AccountId { get; set; }

        [XmlElement("BANKID")]
        public string BankId { get; set; }

        [XmlElement("BRANCHID")]
        public string BranchId { get; set; }

        [XmlElement("ACCTTYPE")]
        public string AccountType { get; set; }
    }

    public class CreditCardAccountInfo
    {
        public CreditCardAccountInfo() { }

        [XmlElement("CCACCTFROM")]
        public CreditCardAccountFrom CreditCardAccountFrom { get; set; }

        [XmlElement("SUPTXDL")]
        public string SupportsDownload { get; set; }

        [XmlElement("XFERSRC")]
        public string TransferSourceEnabled { get; set; }

        [XmlElement("XFERDEST")]
        public string TransferDestinationEnabled { get; set; }

        [XmlElement("SVCSTATUS")]
        public string ActivationStatus { get; set; }
    }

    public class CreditCardAccountFrom
    {
        public CreditCardAccountFrom() { }

        [XmlElement("ACCTID")]
        public string AccountId { get; set; }
    }

    public class InvestmentAccountInfo
    {
        public InvestmentAccountInfo() { }

        [XmlElement("INVACCTFROM")]
        public InvestmentAccountFrom InvAccountFrom { get; set; }

        [XmlElement("USPRODUCTTYPE")]
        public string USProductType { get; set; }

        [XmlElement("CHECKING")]
        public string Checking { get; set; }

        [XmlElement("SVCSTATUS")]
        public string ActivationStatus { get; set; }

        [XmlElement("INVACCTTYPE")]
        public string AccountType { get; set; }

        [XmlElement("OPTIONLEVEL")]
        public string OptionLevel { get; set; }
    }

    public class InvestmentAccountFrom
    {
        public InvestmentAccountFrom() { }

        [XmlElement("BROKERID")]
        public string BrokerId { get; set; }

        [XmlElement("ACCTID")]
        public string AccountId { get; set; }
    }

    public class PinChangeResponseTransaction : TransactionWrapper
    {
        [XmlElement("PINCHRS")]
        public PinChangeResponse PinChangeResponse { get; set; }
    }

    public class PinChangeResponse
    {
        [XmlElement("USERID")]
        public string UserId { get; set; }

        [XmlElement("DTCHANGED")]
        public string DateChanged { get; set; }
    }


}
