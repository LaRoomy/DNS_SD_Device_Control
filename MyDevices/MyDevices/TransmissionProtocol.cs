using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;


/*
 header data:
    - data size
    - data format
    - transmission id
    - encryption type
    - data hash
    - mode (data, confirm)
    - iv
    - data offset
 */

/*
 *      Package Layout:
 * 
 *      1. Data Size (10 bytes)
 *      2. Data Format (1 byte)
 *      3. Data Offset (4 bytes)
 *      3. Transmission ID (4 bytes)
 *      4. Encryption Type (1 byte)
 *      5. Mode (1 byte)
 *      6. Data Hash (32 bytes)
 *      7. IV (24 bytes)
 *      
 * 
 * 
 */

namespace MyDevices
{
    internal class TransmissionProtocol
    {
        public TransmissionProtocol() { }

        public void GenerateTransmissionPacket(string data)
        {
            // Generate a transmission packet
            HashAlgorithmProvider hashAlgorithmProvider = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha256);
            var dataHash = hashAlgorithmProvider.HashData(CryptographicBuffer.ConvertStringToBinary(data, BinaryStringEncoding.Utf8));
        }

        #region fields
        //public TransmissionDataFormat DataFormat { get; set; }
        #endregion
    }

}
