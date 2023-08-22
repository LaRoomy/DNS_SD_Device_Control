using System;

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

namespace TransmissionCTRL
{
    public class TransmissionPackage
    {
        public TransmissionPackage()
        {
            DataFormat = TransmissionDataFormat.PLAIN_TEXT;
            EncryptionType = TransmissionEncryptionType.NONE;
            Mode = TransmissionMode.DATA;
            DataSize = 17;
            TransmissionID = 0;
            IV = TRANSMISSION_PACKAGE_IV_DUMMY;
            Data = TRANSMISSION_PACKAGE_DATA_DUMMY;
        }

        #region fields
        public TransmissionDataFormat DataFormat;
        public TransmissionEncryptionType EncryptionType;
        public TransmissionMode Mode;
        public string Data;
        public string IV;
        public uint DataSize;
        public uint TransmissionID;

        public bool ErrorFlag = false;
        public int ConfirmationControlValue = 0;

        public const string TRANSMISSION_PACKAGE_IV_DUMMY = "0000000000000000";
        public const string TRANSMISSION_PACKAGE_DATA_DUMMY = "DUMMY";
        #endregion

        public string ToTransmissionString()
        {
            try
            {
                this.DataSize = (uint)(17 + this.IV.Length + this.Data.Length);
                string data = this.DataSize.ToString("X8");
                data += Convert.ToString((int)this.DataFormat).PadLeft(1, '0');
                data += (17 + this.IV.Length).ToString("X2");
                data += this.TransmissionID.ToString("X4");
                data += Convert.ToString((int)this.EncryptionType).PadLeft(1, '0');
                data += Convert.ToString((int)this.Mode).PadLeft(1, '0');
                data += this.IV;
                data += this.Data;

                this.ErrorFlag = false;

                return data;
            }
            catch (Exception)
            {
                ErrorFlag = true;
                return null;
            }
        }

        public string ToConfirmationString()
        {
            try
            {
                var dataSize = (uint)17;
                string data = dataSize.ToString("X8");
                data += "0";// TransmissionDataFormat.NONE
                data += "11";// DataOffset 17
                data += this.TransmissionID.ToString("X4");
                data += "0";// TransmissionEncryptionType.NONE
                data += "1";// TransmissionMode.CONFIRM

                this.ErrorFlag = false;

                return data;
            }
            catch (Exception)
            {
                ErrorFlag = true;
                return null;
            }
        }

        public void FromTransmissionString(string data)
        {
            try
            {
                this.DataSize = Convert.ToUInt32(data.Substring(0, 8), 16);
                this.DataFormat = (TransmissionDataFormat)Convert.ToInt32(data.Substring(8, 1));
                int dataOffset = Convert.ToInt32(data.Substring(9, 2), 16);
                this.TransmissionID = Convert.ToUInt32(data.Substring(11, 4), 16);
                this.EncryptionType = (TransmissionEncryptionType)Convert.ToInt32(data.Substring(15, 1));
                this.Mode = (TransmissionMode)Convert.ToInt32(data.Substring(16, 1));

                if (this.Mode != TransmissionMode.CONFIRM)
                {
                    this.IV = data.Substring(17, dataOffset - 17);
                    this.Data = data.Substring(dataOffset);
                }

                this.ErrorFlag = false;
            }
            catch (Exception)
            {
                ErrorFlag = true;
            }
        }
    }

    public enum TransmissionDataFormat { NONE, PLAIN_TEXT, BASE64 };
    public enum TransmissionEncryptionType { NONE, AES, RSA };
    public enum TransmissionMode { DATA, CONFIRM, RSA_PUBKEY, AES_KEY };
}
