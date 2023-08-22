using TransmissionCTRL;

namespace MyDevicesUnitTests
{
    [TestClass]
    public class TransmissionPackageTests
    {
        /*   Package Layout:
         *      
         *      1. Data Size (8 bytes)
         *      2. Data Format (1 byte)
         *      3. Data Offset (2 bytes)
         *      4. Transmission ID (4 bytes)
         *      5. Encryption Type (1 byte)
         *      6. Mode (1 byte)
         *      7. IV (? bytes)
         *      8. Data (? bytes)
         */

        [TestMethod]
        public void TransmissionStringCreation_NoEncryption()
        {
            // Arrange
            var transmissionPackage = new TransmissionPackage();
            transmissionPackage.TransmissionID = 16;
            transmissionPackage.EncryptionType = TransmissionEncryptionType.NONE;
            transmissionPackage.DataFormat = TransmissionDataFormat.PLAIN_TEXT;
            transmissionPackage.Mode = TransmissionMode.DATA;
            transmissionPackage.Data = "Hello Package";

            // Act
            string transmissionString = transmissionPackage.ToTransmissionString();

            // Assert
            Assert.AreEqual("0000002E1210010000000000000000000Hello Package", transmissionString);
        }

        [TestMethod]
        public void TransmissionStringCreation_WithAESEncryption()
        {
            // Arrange
            var transmissionPackage = new TransmissionPackage();
            transmissionPackage.TransmissionID = 287;
            transmissionPackage.EncryptionType = TransmissionEncryptionType.AES;
            transmissionPackage.DataFormat = TransmissionDataFormat.BASE64;
            transmissionPackage.Mode = TransmissionMode.DATA;
            transmissionPackage.Data = "WADDbO/CpcxoRYt2kLXUiPmd5pwayzzlcj1Uv0rjJxw=";
            transmissionPackage.IV = "tWERwMWSADZI3BgHOD5+8g==";

            // Act
            string transmissionString = transmissionPackage.ToTransmissionString();

            // Assert
            Assert.AreEqual("00000055229011F10tWERwMWSADZI3BgHOD5+8g==WADDbO/CpcxoRYt2kLXUiPmd5pwayzzlcj1Uv0rjJxw=", transmissionString);
        }

        [TestMethod]
        public void TransmissionStringCreation_RSAPublicKeyTransmission()
        {
            // Arrange
            var transmissionPackage = new TransmissionPackage();
            transmissionPackage.TransmissionID = 1;
            transmissionPackage.EncryptionType = TransmissionEncryptionType.NONE;
            transmissionPackage.DataFormat = TransmissionDataFormat.BASE64;
            transmissionPackage.Mode = TransmissionMode.RSA_PUBKEY;
            transmissionPackage.Data = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAw1+6SnGu4WUxe87yiQy9fzx66UDZp1+O\r\nDmvda9dVTcFnmqPpAqcHBocgTMZ5hGUhnzelbwo6KFH5isG2zQyXcjyD3PZAuBu0UXmiAORyjNDn\r\ntYV5nb6KWiYi2/RUEFkvSLQcxADAzgxH9Fs8ObWwOlcf+mnQfoK1gZWCZFhYOW7sZ035QotDh00+\r\nYDJyv6aygiwlEUY+liNFRHYzvD4tGI9MUGci7n6G2ZDgWzrjY5xhNgPS+VWZG/ie2p4bA7GO+EC0\r\nUsYegSIPCENxqBYMGlQ8f9Jby+CKDlDYCit/c7SNDhnNZP8Jja5JjWk8fOzyvVRjD14leSyjrK9C\r\nZmEicQIDAQAB";

            // Act
            string transmissionString = transmissionPackage.ToTransmissionString();

            // Assert
            Assert.AreEqual(
                "000001B32210001020000000000000000MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAw1+6SnGu4WUxe87yiQy9fzx66UDZp1+O\r\nDmvda9dVTcFnmqPpAqcHBocgTMZ5hGUhnzelbwo6KFH5isG2zQyXcjyD3PZAuBu0UXmiAORyjNDn\r\ntYV5nb6KWiYi2/RUEFkvSLQcxADAzgxH9Fs8ObWwOlcf+mnQfoK1gZWCZFhYOW7sZ035QotDh00+\r\nYDJyv6aygiwlEUY+liNFRHYzvD4tGI9MUGci7n6G2ZDgWzrjY5xhNgPS+VWZG/ie2p4bA7GO+EC0\r\nUsYegSIPCENxqBYMGlQ8f9Jby+CKDlDYCit/c7SNDhnNZP8Jja5JjWk8fOzyvVRjD14leSyjrK9C\r\nZmEicQIDAQAB",
                transmissionString
            );
        }

        [TestMethod]
        public void ConfirmationStringCreation()
        {
            // Arrange
            var transmissionPackage = new TransmissionPackage();
            transmissionPackage.TransmissionID = 16;
            transmissionPackage.EncryptionType = TransmissionEncryptionType.RSA;
            transmissionPackage.DataFormat = TransmissionDataFormat.BASE64;
            transmissionPackage.Mode = TransmissionMode.DATA;
            transmissionPackage.Data = "Hello Package";

            // Act
            string transmissionString = transmissionPackage.ToConfirmationString();

            // Assert
            Assert.AreEqual("00000011011001001", transmissionString);
            Assert.AreEqual(false, transmissionPackage.ErrorFlag);
        }

        [TestMethod]
        public void PackageCreationFromString()
        {
            // Arrange
            string transmissionString = "0000002E1210010000000000000000000Hello Package";
            var transmissionPackage = new TransmissionPackage();

            // Act
            transmissionPackage.FromTransmissionString(transmissionString);

            // Assert
            Assert.AreEqual((uint)16, transmissionPackage.TransmissionID);
            Assert.AreEqual("Hello Package", transmissionPackage.Data);
            Assert.AreEqual(TransmissionDataFormat.PLAIN_TEXT, transmissionPackage.DataFormat);
            Assert.AreEqual((uint)46, transmissionPackage.DataSize);
            Assert.AreEqual(TransmissionEncryptionType.NONE, transmissionPackage.EncryptionType);
            Assert.AreEqual(false, transmissionPackage.ErrorFlag);
            Assert.AreEqual(TransmissionMode.DATA, transmissionPackage.Mode);
        }

        [TestMethod]
        public void PackageCreationFromString_AESKeyTransmission()
        {
            // Arrange
            string transmissionString = "00000181229000323AAECAwQFBgcICQoLDA0ODw==djHM63hJ4MkgUPSVYtdkOC7HNBJVE7zVpSnIbCA+vaIwhQC2IlnALP67aib21Fccosx0HE+sRuoIiFfN8r9mc3S6Q+OM3g0H3a0IKz9xOspF8UePkvjF1krBvySfGSBV5cc0agjYI2ajMFts1F527kbqLEmCdqCS5KWDtSvD4cXC/iHDziLs0Nc8efXlt3iCVYxxsDoP5TGS9ya1ttGYMA0hsX0RSmyanI89wOT6G5ebKCVsaZEwYIfBEtuUwklvGCCYTCpNUZvb2HrFAHKmPJ6yi/7yMNr0y649vLRIBSa+oK2SsOaNzfW/Gx3ADqj/WIo9AIXmabzVmPjwd+uCrQ==";
            var transmissionPackage = new TransmissionPackage();
            var expectedIV = "AAECAwQFBgcICQoLDA0ODw==";
            var expectedData = "djHM63hJ4MkgUPSVYtdkOC7HNBJVE7zVpSnIbCA+vaIwhQC2IlnALP67aib21Fccosx0HE+sRuoIiFfN8r9mc3S6Q+OM3g0H3a0IKz9xOspF8UePkvjF1krBvySfGSBV5cc0agjYI2ajMFts1F527kbqLEmCdqCS5KWDtSvD4cXC/iHDziLs0Nc8efXlt3iCVYxxsDoP5TGS9ya1ttGYMA0hsX0RSmyanI89wOT6G5ebKCVsaZEwYIfBEtuUwklvGCCYTCpNUZvb2HrFAHKmPJ6yi/7yMNr0y649vLRIBSa+oK2SsOaNzfW/Gx3ADqj/WIo9AIXmabzVmPjwd+uCrQ==";

            // Act
            transmissionPackage.FromTransmissionString(transmissionString);

            // Assert
            Assert.AreEqual((uint)3, transmissionPackage.TransmissionID);
            Assert.AreEqual(expectedData, transmissionPackage.Data);
            Assert.AreEqual(TransmissionDataFormat.BASE64, transmissionPackage.DataFormat);
            Assert.AreEqual((uint)385, transmissionPackage.DataSize);
            Assert.AreEqual(TransmissionEncryptionType.RSA, transmissionPackage.EncryptionType);
            Assert.AreEqual(false, transmissionPackage.ErrorFlag);
            Assert.AreEqual(TransmissionMode.AES_KEY, transmissionPackage.Mode);
            Assert.AreEqual(expectedIV, transmissionPackage.IV);
        }
    }
}