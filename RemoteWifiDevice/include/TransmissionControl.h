#include <Arduino.h>
#include <mbedtls/pk.h>
#include <mbedtls/rsa.h>
#include <mbedtls/aes.h>
#include "mbedtls/entropy.h"
#include "mbedtls/ctr_drbg.h"
#include "mbedtls/error.h"
#include "mbedtls/base64.h"
#include "mbedtls/aes.h"
#include "ItemCollection.h"

#define TRANSMISSION_PACKAGE_IV_DUMMY "0000000000000000";
#define TRANSMISSION_PACKAGE_DATA_DUMMY "DUMMY";

#define STATUS_REQUEST_RESPONSE "rs:status:active"

/*   Transmission Package Layout:
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

enum TransmissionDataFormat { TDF_NONE, PLAIN_TEXT, BASE64 };
enum TransmissionEncryptionType { TET_NONE, AES, RSA };
enum TransmissionMode { DATA, CONFIRM, RSA_PUBKEY, AES_KEY };

class ITransmissionControlInterface
{   
public:
    virtual void OutGateway(const String& data) = 0;
    virtual void OnDataDecoded(const String& data) = 0;
    virtual void OnUnencryptedDataReceived(const String& data) = 0;
};

class TransmissionPackage
{
public:
    TransmissionPackage();
    TransmissionPackage(const TransmissionPackage& other);

    TransmissionMode mode;
    TransmissionDataFormat dataFormat;
    TransmissionEncryptionType encryptionType;
    String data;
    String iv;
    unsigned int dataSize;
    unsigned int transmissionID;

    bool errorFlag;
    unsigned int confirmationParam;

    String ToTransmissionString();
    String ToConfirmationString() const;
    void FromTransmissionString(const String& transmissionString);

    TransmissionPackage& operator=(const TransmissionPackage& other);
};

void printMBED_TLSError(int errorCode);

class TransmissionControl
{
public:
    TransmissionControl();

    void OnDataReceived(const String& data);
    void SendData(const String& data, bool encrypt);
    void SetInterface(ITransmissionControlInterface* interface);
    void SetDeviceName(const String& name);

    void OnClientConnected();
    void OnClientDisconnected();

    void OnLoop();

private:
    TransmissionPackage currentPackage;
    ITransmissionControlInterface* interface;

    itemCollection<TransmissionPackage> transmissionQueue;

    String rsa_key;
    String device_name;

    mbedtls_pk_context pk;
    mbedtls_aes_context aes;

    mbedtls_ctr_drbg_context ctr_drbg;
    mbedtls_entropy_context entropy;

    bool pk_context_initialized;
    bool connection_state;

    unsigned int transmissionID;
    unsigned char aes_key[32];

    void onRSAKeyReceived(const String& data);
    bool readAndFormatRSAKey(const String& data);
    String decryptReceivedDataWithAESCbc(const String& data, const String& _iv);
    bool createAESData();
    bool generateRandomIV(unsigned char* _iv);
    String EncryptDataWithAES(const String& data, String& _iv_out);
    void decodeAndProcessEncryptedData(const TransmissionPackage& package);
    bool internalDataProcessing(const String& data);
    void processTransmission(const String& transmissionString);
    void confirmPackageReception(const TransmissionPackage& package);
};