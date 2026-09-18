using System.Xml.Linq;
using NUnit.Framework;
using Walkabout.Ofx;

namespace Walkabout.Tests
{
    /// <summary>
    /// OFX.Deserialize is the OFX response object model's entry point. It moved into
    /// MyMoney.Business in Task 8, so these tests deliberately touch nothing but
    /// Walkabout.Ofx types: no MyMoney database, no dispatcher, no UI callback, no WPF.
    /// </summary>
    [TestFixture]
    public class OfxObjectModelTests
    {
        // A minimal but real OFX 2.x signon response: the shape every OfxRequest round trip
        // starts with. Values are stringly-typed in the object model exactly as the spec
        // defines them, so they are asserted verbatim.
        private const string SignonResponse = @"<OFX>
  <SIGNONMSGSRSV1>
    <SONRS>
      <STATUS>
        <CODE>0</CODE>
        <SEVERITY>INFO</SEVERITY>
      </STATUS>
      <DTSERVER>20260918120000.000[-8:PST]</DTSERVER>
      <LANGUAGE>ENG</LANGUAGE>
      <FI>
        <ORG>Example Bank</ORG>
        <FID>1234</FID>
      </FI>
    </SONRS>
  </SIGNONMSGSRSV1>
</OFX>";

        [Test]
        public void Deserialize_SignonResponse_PopulatesSignonMessageSet()
        {
            OFX ofx = OFX.Deserialize(XDocument.Parse(SignonResponse));

            Assert.That(ofx, Is.Not.Null);
            SignOnResponse sonrs = ofx.SignOnMessageResponse.SignOnResponse;
            Assert.That(sonrs.OfxStatus.Code, Is.EqualTo(0));
            Assert.That(sonrs.OfxStatus.Severity, Is.EqualTo("INFO"));
            Assert.That(sonrs.Language, Is.EqualTo("ENG"));
            Assert.That(sonrs.ServerDate, Is.EqualTo("20260918120000.000[-8:PST]"));
            Assert.That(sonrs.FinancialInstitution.Organization, Is.EqualTo("Example Bank"));
            Assert.That(sonrs.FinancialInstitution.FID, Is.EqualTo("1234"));

            // Message sets the server did not send come back null rather than empty objects,
            // which is what OfxRequest's "did the server answer this request?" checks rely on.
            Assert.That(ofx.SignUpMessageResponse, Is.Null);
            Assert.That(ofx.ProfileMessageSet, Is.Null);
        }

        [Test]
        public void Deserialize_SignonFailure_PreservesServerStatusCode()
        {
            // 15500 is OfxErrorCode.SignonInvalid; the object model keeps CODE as a plain int
            // so that OfxRequest can cast it to OfxErrorCode and report it to the user.
            string failure = SignonResponse
                .Replace("<CODE>0</CODE>", "<CODE>15500</CODE>")
                .Replace("<SEVERITY>INFO</SEVERITY>", "<SEVERITY>ERROR</SEVERITY><MESSAGE>Invalid password</MESSAGE>");

            OFX ofx = OFX.Deserialize(XDocument.Parse(failure));

            OfxStatus status = ofx.SignOnMessageResponse.SignOnResponse.OfxStatus;
            Assert.That(status.Code, Is.EqualTo(15500));
            Assert.That((OfxErrorCode)status.Code, Is.EqualTo(OfxErrorCode.SignonInvalid));
            Assert.That(status.Severity, Is.EqualTo("ERROR"));
            Assert.That(status.Message, Is.EqualTo("Invalid password"));
        }

        [Test]
        public void Deserialize_NonOfxDocument_ThrowsOfxExceptionCarryingTheResponse()
        {
            var doc = XDocument.Parse("<NotOfx><Garbage/></NotOfx>");

            // Verified against OFX.Deserialize: any XmlSerializer failure (here, an unexpected
            // root element) is rethrown as OfxException with the offending document attached,
            // which is what the download error report renders.
            OfxException ex = Assert.Throws<OfxException>(() => OFX.Deserialize(doc));
            Assert.That(ex.Message, Is.EqualTo("Error parsing OFX response"));
            Assert.That(ex.Code, Is.EqualTo("Error"));
            Assert.That(ex.Response, Does.Contain("NotOfx"));
        }

        [Test]
        public void Deserialize_MalformedElementValue_ThrowsOfxException()
        {
            // CODE is an int in the object model; a non-numeric value is a server bug rather
            // than a client one, and must surface as the same OfxException rather than as a
            // raw InvalidOperationException from XmlSerializer.
            var doc = XDocument.Parse(SignonResponse.Replace("<CODE>0</CODE>", "<CODE>not-a-number</CODE>"));

            Assert.Throws<OfxException>(() => OFX.Deserialize(doc));
        }
    }
}
