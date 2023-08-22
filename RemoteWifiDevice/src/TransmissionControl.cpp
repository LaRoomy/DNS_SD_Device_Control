#include "TransmissionControl.h"

// timer params
long checkupTimer = 0;
long checkupTimeOut = 500;

TransmissionPackage::TransmissionPackage()
{
    mode = TransmissionMode::DATA;
    dataFormat = TransmissionDataFormat::PLAIN_TEXT;
    encryptionType = TransmissionEncryptionType::TET_NONE;
    data = TRANSMISSION_PACKAGE_DATA_DUMMY;
    iv = TRANSMISSION_PACKAGE_IV_DUMMY;
    dataSize = 0;
    transmissionID = 0;
    errorFlag = false;
}

TransmissionPackage::TransmissionPackage(const TransmissionPackage& other)
{
    this->mode = other.mode;
    this->dataFormat = other.dataFormat;
    this->encryptionType = other.encryptionType;
    this->data = other.data;
    this->iv = other.iv;
    this->dataSize = other.dataSize;
    this->transmissionID = other.transmissionID;
    this->errorFlag = other.errorFlag;
    this->confirmationParam = 0;
}

String TransmissionPackage::ToTransmissionString()
{
    char buffer[48] = { 0 };

    String transmissionString = "";
    this->dataSize = 17 + this->data.length() + this->iv.length();
    sprintf(buffer, "%08x%01d%02x%04x%01d%01d", this->dataSize, this->dataFormat, 17 + this->iv.length(), this->transmissionID, this->encryptionType, this->mode);
    transmissionString += buffer;
    transmissionString += this->iv;
    transmissionString += this->data;

    return transmissionString;
}

void TransmissionPackage::FromTransmissionString(const String& data)
{
    if(data.length() < 17)
    {
        this->errorFlag = true;
        return;
    }
    else
    {
        this->errorFlag = false;
        unsigned int dataOffset = 0;

        auto ret = sscanf(data.c_str(), "%08x%01d%02x%04x%01d%01d", &this->dataSize, &this->dataFormat, &dataOffset, &this->transmissionID, &this->encryptionType, &this->mode);
        if(ret != 6)
        {
            this->errorFlag = true;
            return;
        }
        else
        {
            if(this->mode != TransmissionMode::CONFIRM)
            {
                this->iv = data.substring(17, dataOffset);
                this->data = data.substring(dataOffset);
            }
        }
    }
}

String TransmissionPackage::ToConfirmationString() const
{
    char buffer[48] = { 0 };

    String confirmationString = "";
    sprintf(buffer, "%08x%01d%02x%04x%01d%01d", 17, TransmissionDataFormat::TDF_NONE, 17, this->transmissionID, TransmissionEncryptionType::TET_NONE, TransmissionMode::CONFIRM);
    confirmationString += buffer;

    return confirmationString;    
}

TransmissionPackage& TransmissionPackage::operator=(const TransmissionPackage& other)
{
    this->mode = other.mode;
    this->dataFormat = other.dataFormat;
    this->encryptionType = other.encryptionType;
    this->data = other.data;
    this->iv = other.iv;
    this->dataSize = other.dataSize;
    this->transmissionID = other.transmissionID;
    this->errorFlag = other.errorFlag;

    return *this;
}

void printMBED_TLSError(int errorCode)
{
    char buf[1024];
    mbedtls_strerror(errorCode, buf, sizeof(buf));
    Serial.println(buf);
}

TransmissionControl::TransmissionControl()
: pk_context_initialized(false), interface(nullptr), connection_state(false), transmissionID(0)
{
    memset(this->aes_key, 0, sizeof(this->aes_key));
    checkupTimer = millis();
}

void TransmissionControl::SetInterface(ITransmissionControlInterface* _interface)
{
    this->interface = _interface;
}

void TransmissionControl::SetDeviceName(const String& name)
{
    this->device_name = name;
}

void TransmissionControl::SendData(const String& data, bool encrypt)
{
    //Serial.println("Sending data:");
    //Serial.println(data);

    if(!encrypt)
    {
        TransmissionPackage transmissionPackage;
        transmissionPackage.data = data;
        transmissionPackage.dataFormat = TransmissionDataFormat::PLAIN_TEXT;
        transmissionPackage.encryptionType = TransmissionEncryptionType::TET_NONE;
        transmissionPackage.mode = TransmissionMode::DATA;
        transmissionPackage.transmissionID = this->transmissionID++;
        
        if(this->transmissionQueue.GetCount() > 0)
        {
            // if there are unconfirmed transmissions in the queue, add the new package to the queue, but don't send it out
            this->transmissionQueue += transmissionPackage;
        }
        else
        {
            if(this->interface != nullptr)
            {
                this->interface->OutGateway(transmissionPackage.ToTransmissionString());
                this->transmissionQueue += transmissionPackage;
            }
        }
    }
    else
    {
        TransmissionPackage transmissionPackage;
        transmissionPackage.dataFormat = TransmissionDataFormat::BASE64;
        transmissionPackage.encryptionType = TransmissionEncryptionType::AES;
        transmissionPackage.mode = TransmissionMode::DATA;
        transmissionPackage.transmissionID = this->transmissionID++;
        transmissionPackage.data = this->EncryptDataWithAES(data, transmissionPackage.iv);

        if(this->transmissionQueue.GetCount() > 0)
        {
            // if there are unconfirmed transmissions in the queue, add the new package to the queue, but don't send it out
            this->transmissionQueue += transmissionPackage;
        }
        else
        {
            if(this->interface != nullptr)
            {
                // send the data out gateway
                this->interface->OutGateway(transmissionPackage.ToTransmissionString());
                // add the package to the transmission queue
                this->transmissionQueue += transmissionPackage;
            }
        }
    }
}

void TransmissionControl::OnClientConnected()
{
    if(!this->connection_state)
    {
        this->connection_state = true;

        // do something on connection state change to 'connected'
    }
}

void TransmissionControl::OnClientDisconnected()
{
    if(this->pk_context_initialized)
    {
        mbedtls_pk_free(&this->pk);
        this->pk_context_initialized = false;
    }
    if(this->connection_state)
    {
        this->connection_state = false;

        // do something on connection state change to 'disconnected'
    }
}

void TransmissionControl::OnDataReceived(const String& data)
{
    // since the server is faster than the client, successive transmissions could be appended in the 
    // input queue, so check if there are multiple transmissions in the input string

    auto dLen = data.length();
    if(dLen >= 8)
    {
        unsigned int packageSize = 0;
        itemCollection<String> inputStrings;

        auto ret = sscanf(data.c_str(), "%08x", &packageSize);
        if(ret > 0)
        {
            if(dLen > packageSize)
            {
                // size mismatch ->
                // must be more than one transmission in the input string
                bool hold = true;
                String subData = data.substring(packageSize);
                inputStrings.AddItem(data.substring(0, packageSize));

                while(hold)
                {
                    auto subDataLen = subData.length();
                    if(subDataLen >= 8)
                    {
                        unsigned int subPackageSize = 0;
                        ret = sscanf(subData.c_str(), "%08x", &subPackageSize);
                        if(ret > 0)
                        {
                            if(subDataLen > subPackageSize)
                            {
                                // size mismatch ->
                                // must be more than one transmission in the string
                                subData = subData.substring(subPackageSize);
                                inputStrings.AddItem(subData.substring(0, subPackageSize));
                            }
                            else
                            {
                                // size match ->
                                // only one transmission in the string
                                inputStrings.AddItem(subData);
                                hold = false;
                            }
                        }
                        else
                        {
                            Serial.println("sscanf - error - sub");
                            hold = false;
                        }
                    }
                    else
                    {
                        Serial.println("transmission length error - sub");
                        hold = false;
                    }
                }

            }
            else
            {
                // size match ->
                // only one transmission in the input string
                this->processTransmission(data);
                return;
            }
        }

        for(unsigned int i = 0; i < inputStrings.GetCount(); i++)
        {
            this->processTransmission(inputStrings.GetAt(i));
        }
    }
}

bool TransmissionControl::readAndFormatRSAKey(const String& data)
{
    if(data.length() > 0)
    {
        // set public key start-sequence for DER encoding
        this->rsa_key = "-----BEGIN PUBLIC KEY-----\n";
        // set public key in DER encoding
        this->rsa_key += data;
        // set public key end-sequence for DER encoding
        this->rsa_key += "\n-----END PUBLIC KEY-----\n";

        return true;
    }
    else
    {
        return false;
    }
}

void TransmissionControl::onRSAKeyReceived(const String& data)
{
    // extract the rsa params from the transmission
    auto res = this->readAndFormatRSAKey(data);
    if(res)
    {
        Serial.println("RSA public key in PEM format:");
        Serial.println(rsa_key);

        // init rsa context
        mbedtls_pk_init(&this->pk);
        this->pk_context_initialized = true;

        auto ret = mbedtls_pk_parse_public_key(&this->pk, (const unsigned char*)this->rsa_key.c_str(), this->rsa_key.length() + 1);
        if(ret != 0)
        {
            Serial.println("Error: RSA key could not be parsed!");
            printMBED_TLSError(ret);
        }
        else
        {
            Serial.println("RSA key successfully parsed!");

            // create the aes key and iv for this session
            if(createAESData())
            {  
                size_t outLen = 0;
                unsigned char output[256];

                // encrypt aes key with rsa public key
                ret = mbedtls_pk_encrypt(&this->pk, this->aes_key, sizeof(this->aes_key), output, &outLen, 256, mbedtls_ctr_drbg_random, &this->ctr_drbg);

                //ret = mbedtls_base64_encode(enc_dest, sizeof(enc_dest), &outLen, aes_data, sizeof(aes_data));
                if(ret != 0)
                {                            
                    Serial.print("Error: AES data could not be encrypted!");
                    printMBED_TLSError(ret);
                }
                else
                {
                    Serial.println("AES data successfully encrypted!");

                    unsigned char enc_dest[1024] = {0};
                                        
                    // convert aes data to base64 string
                    ret = mbedtls_base64_encode(enc_dest, sizeof(enc_dest), &outLen, output, outLen);
                    if(ret != 0)
                    {
                        Serial.println("Error: AES data could not be encoded!");
                        printMBED_TLSError(ret);
                    }
                    else
                    {
                        Serial.println("AES data successfully encoded to base64!");

                        // encode iv to base 64
                        unsigned char ivBuffer[256] = {0};
                        size_t bLen = 0;

                        // dummy iv (not used in this type of transmission)
                        byte iv[16] = {0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15};

                        ret = mbedtls_base64_encode(ivBuffer, sizeof(ivBuffer), &bLen, iv, sizeof(iv));
                        if(ret != 0)
                        {
                            Serial.println("Error: AES iv could not be encoded!");
                            printMBED_TLSError(ret);
                        }

                        String enc_data = (char*)enc_dest;

                        TransmissionPackage transmissionPackage;
                        transmissionPackage.mode = TransmissionMode::AES_KEY;
                        transmissionPackage.encryptionType = TransmissionEncryptionType::RSA;
                        transmissionPackage.dataFormat = TransmissionDataFormat::BASE64;
                        transmissionPackage.data = enc_data;
                        transmissionPackage.iv = (char*)ivBuffer;
                        transmissionPackage.transmissionID = this->transmissionID++;

                        // TODO: new method "sendPackage" ???

                        // send transmission package
                        if(this->interface != nullptr)
                        {
                            this->interface->OutGateway(
                                transmissionPackage.ToTransmissionString()
                            );
                            this->transmissionQueue.AddItem(transmissionPackage);
                        }
                    }
                }
            }
        }
    }
    else
    {
        Serial.println("Error: RSA parameters could not be read!");
    }
}

String TransmissionControl::decryptReceivedDataWithAESCbc(const String& data, const String& _iv)
{
    String result = "";

    if(data.length() > 0 && _iv.length() > 0)
    {
        size_t reqLen = 0;

        mbedtls_base64_decode(nullptr, 0, &reqLen, (const unsigned char*)_iv.c_str(), _iv.length());

        if(reqLen > 0)
        {
            auto ivBuffer = new unsigned char[reqLen];
            if(ivBuffer != nullptr)
            {
                memset(ivBuffer, 0, reqLen);

                auto ret = mbedtls_base64_decode(ivBuffer, reqLen, &reqLen, (const unsigned char*)_iv.c_str(), _iv.length());
                if(ret != 0)
                {
                    Serial.println("EncryptDataWithAES:Error: Base64 iv decode failed with:");
                    printMBED_TLSError(ret);
                }
                else
                {
                    size_t outlen;

                    auto ret =
                        mbedtls_base64_decode(nullptr, 0, &outlen, (const unsigned char*)data.c_str(), data.length());
                    
                    if(outlen > 0)
                    {
                        //Serial.print("Base64 decoded data length: ");
                        //Serial.println(outlen);

                        unsigned char *dataBuffer = new unsigned char[outlen];
                        if(dataBuffer != nullptr)
                        {

                            ret =
                                mbedtls_base64_decode(dataBuffer, outlen, &outlen, (const unsigned char*)data.c_str(), data.length());

                            if(ret != 0)
                            {
                                Serial.println("Error: base64 data decode failed!");
                                printMBED_TLSError(ret);
                                Serial.print("Source data: ");
                                Serial.println(data);
                            }
                            else
                            {
                                ret = mbedtls_aes_setkey_enc(&this->aes, this->aes_key, 256);
                                if(ret != 0)
                                {
                                    Serial.println("Error: Setting AES key failed!");
                                }
                                else
                                {
                                    unsigned char dataReceiver[1024] = {0}; // TODO: dynamic size

                                    ret = mbedtls_aes_crypt_cbc(&this->aes, MBEDTLS_AES_DECRYPT, outlen, ivBuffer, dataBuffer, dataReceiver);
                                    if(ret != 0)
                                    {
                                        Serial.println("Error: AES decryption failed!");
                                    }
                                    else
                                    {
                                        //Serial.print("AES data decryption succeeded: ");
                                        //Serial.println((char*)dataReceiver);

                                        result = (char*)dataReceiver;
                                    }
                                }                    
                            }
                            delete[] dataBuffer;
                        }
                    }
                    else
                    {
                        Serial.println("Error: base64 data out-len was zero");
                    }
                }
                delete[] ivBuffer;
            }
        }
    }
    return result;
}

bool TransmissionControl::createAESData()
{
    // initialize entropy pool and random number generator
    mbedtls_entropy_init(&this->entropy);
    mbedtls_ctr_drbg_init(&this->ctr_drbg);

    // generate random iv
    unsigned char iv[16] = {0};
    this->generateRandomIV(iv);

    // seed random number generator
    auto ret = mbedtls_ctr_drbg_seed(&this->ctr_drbg, mbedtls_entropy_func, &this->entropy, iv, (size_t)16);
    if(ret != 0)
    {
        Serial.print("Error: Random data generation failed with: ");
        printMBED_TLSError(ret);
        return false;
    }
    else
    {
        // generate random aes key
        ret = mbedtls_ctr_drbg_random(&this->ctr_drbg, this->aes_key, 32);
        if(ret != 0)
        {
            Serial.print("Error: aes key generation failed with: ");
            printMBED_TLSError(ret);
            return false;
        }
        else
        {
            Serial.println("AES key successfully generated!");
            return true;
        }
    }
}

bool TransmissionControl::generateRandomIV(unsigned char* _iv)
{
    if(_iv == nullptr)
    {
        return false;
    }
    else
    {
        for(unsigned int i = 0; i < 16; i++)
        {
            _iv[i] = (unsigned char)random(0, 255);
        }
        return true;
    }
}

String TransmissionControl::EncryptDataWithAES(const String& data, String& _iv_out)
{
    String enc_data;

    if(data.length() > 0)
    {
        auto ret = mbedtls_aes_setkey_enc(&this->aes, this->aes_key, 256);
        if(ret != 0)
        {
            Serial.println("EncryptDataWithAES:Error: Setting AES key failed with:");
            printMBED_TLSError(ret);
        }
        else
        {
            // make sure the buffer is a multiple of 16
            size_t len = data.length() + (16 - (data.length() % 16));

            unsigned char* enc_receiver = new unsigned char[len];

            if(enc_receiver != nullptr)
            {
                unsigned char iv[16] = {0};
                unsigned char iv_copy[16] = {0};

                this->generateRandomIV(iv);

                // copy iv, because the iv is changed in mbedtls_aes_crypt_cbc, but we need it for the receiver
                for(unsigned int i = 0; i < 16; i++)
                {
                    iv_copy[i] = iv[i];
                }

                // encrypt data
                ret = mbedtls_aes_crypt_cbc(&this->aes, MBEDTLS_AES_ENCRYPT, len, iv, (const unsigned char*)data.c_str(), enc_receiver);
                if(ret != 0)
                {
                    Serial.println("EncryptDataWithAES:Error: AES encryption failed with:");
                    printMBED_TLSError(ret);
                }
                else
                {
                    size_t reqLen;

                    // calculate buffer size for base64 encoding
                    mbedtls_base64_encode(nullptr, 0, &reqLen, enc_receiver, len);

                    unsigned char *encDataBuffer = new unsigned char[reqLen];
                    if(encDataBuffer != nullptr)
                    {
                        memset(encDataBuffer, 0, reqLen);

                        // convert encrypted data to base64
                        ret = mbedtls_base64_encode(encDataBuffer, reqLen, &reqLen, enc_receiver, len);
                        if(ret != 0)
                        {
                            Serial.println("EncryptDataWithAES:Error: base64 encoding of encrypted data failed with:");
                            printMBED_TLSError(ret);
                        }
                        else
                        {
                            enc_data = (char*)encDataBuffer;

                            mbedtls_base64_encode(nullptr, 0, &reqLen, iv_copy, 16);

                            char *ivBuffer = new char[reqLen];
                            if(ivBuffer != nullptr)
                            {
                                memset(ivBuffer, 0, reqLen);

                                ret = mbedtls_base64_encode((unsigned char*)ivBuffer, reqLen, &reqLen, iv_copy, 16);
                                if(ret != 0)
                                {
                                    Serial.println("EncryptDataWithAES:Error: base64 encoding of iv failed with:");
                                    printMBED_TLSError(ret);
                                }
                                else
                                {
                                    _iv_out = ivBuffer;
                                }
                                delete[] ivBuffer;
                            }
                        }
                        delete[] encDataBuffer;
                    }
                }
                delete[] enc_receiver;
            }
        }
    }
    else
    {
        Serial.println("EncryptDataWithAES:Error: Data or iv length was zero!");
    }
    return enc_data;
}

void TransmissionControl::decodeAndProcessEncryptedData(const TransmissionPackage& package)
{
    if(package.encryptionType == TransmissionEncryptionType::AES)
    {
        auto dec_data = this->decryptReceivedDataWithAESCbc(package.data, package.iv);
        if(!this->internalDataProcessing(dec_data))
        {
            // if the data was not processed internally, send it to the next layer
            if(this->interface != nullptr)
            {
                this->interface->OnDataDecoded(dec_data);
            }
        }
    }
    else
    {
        if(this->interface != nullptr)
        {
            this->interface->OnUnencryptedDataReceived(package.data);
        }
    }
}

bool TransmissionControl::internalDataProcessing(const String& data)
{
    // return true to indicate that the data was processed

    if(data.startsWith("get-name"))
    {
        String devNameResponse = "set-name:";
        devNameResponse += this->device_name;
        this->SendData(devNameResponse, true);
        return true;
    }
    else if(data.startsWith("rq:status"))
    {
        this->SendData(STATUS_REQUEST_RESPONSE, true);
        return true;
    }
    return false;
}

void TransmissionControl::processTransmission(const String& transmissionString)
{
    TransmissionPackage transmissionPackage;
    transmissionPackage.FromTransmissionString(transmissionString);
    
    if(!transmissionPackage.errorFlag)
    {
        switch (transmissionPackage.mode)
        {
        case TransmissionMode::DATA:
            this->confirmPackageReception(transmissionPackage);
            this->decodeAndProcessEncryptedData(transmissionPackage);
            break;
        case TransmissionMode::CONFIRM:
            // NOTE: do not confirm the confirmation, because it will cause an infinite loop

            //Serial.print("Confirmation received for package with ID: ");
            //Serial.println(transmissionPackage.transmissionID);

            // remove the confirmed package from the transmission queue
            for(unsigned int i = 0; i < this->transmissionQueue.GetCount(); i++)
            {
                if(this->transmissionQueue.GetAt(i).transmissionID == transmissionPackage.transmissionID)
                {
                    this->transmissionQueue.RemoveAt(i);
                    break;
                }
            }
            // if there are waiting transmissions in the queue, send the next one
            if(this->transmissionQueue.GetCount() > 0)
            {
                if(this->interface != nullptr)
                {
                    this->interface->OutGateway(
                        this->transmissionQueue.GetAt(0).ToTransmissionString()
                    );
                }
            }
            break;
        case TransmissionMode::AES_KEY:
            // not valid on this side, but nonetheless confirm the reception
            this->confirmPackageReception(transmissionPackage);
            break;
        case TransmissionMode::RSA_PUBKEY:
            this->confirmPackageReception(transmissionPackage);
            this->onRSAKeyReceived(transmissionPackage.data);
            break;
        default:
            break;
        }
    }
    else
    {
        Serial.println("TransmissionPackage error while reading from input string");
    }
}

void TransmissionControl::confirmPackageReception(const TransmissionPackage& package)
{
    auto confirmationString = package.ToConfirmationString();
    if(this->interface != nullptr)
    {
        this->interface->OutGateway(confirmationString);
    }
}

void TransmissionControl::OnLoop()
{
    if(millis() > (unsigned long)(checkupTimeOut + checkupTimer))
    {
        checkupTimer = millis();

        if(this->connection_state == true)
        {
            // check if there are unconfirmed packages in the queue
            if (this->transmissionQueue.GetCount() > 0)
            {
                // check if the first package in the queue was previously marked as unconfirmed
                if(this->transmissionQueue.GetAt(0).confirmationParam == 0)
                {
                    // in the first instance, mark the package as unconfirmed
                    this->transmissionQueue.GetAt(0).confirmationParam = 1;
                }
                else
                {
                    // the package was previously marked as unconfirmed, check if it was sent 3 times
                    if(this->transmissionQueue.GetAt(0).confirmationParam >= 4)
                    {
                        // if the package was sent 3 times, remove it from the queue
                        this->transmissionQueue.RemoveAt(0);

                        // if there are still packages in the queue, send the next one
                        if(this->transmissionQueue.GetCount() > 0)
                        {
                            if(this->interface != nullptr)
                            {
                                this->interface->OutGateway(
                                    this->transmissionQueue.GetAt(0).ToTransmissionString()
                                );
                            }
                        }
                    }
                    else
                    {
                        Serial.print(">>> Resending package with ID: ");
                        Serial.println(this->transmissionQueue.GetAt(0).transmissionID);
                        Serial.print("This is tryout: ");
                        Serial.println(this->transmissionQueue.GetAt(0).confirmationParam);

                        this->transmissionQueue.GetAt(0).confirmationParam++;

                        // send the package again
                        if(this->interface != nullptr)
                        {
                            this->interface->OutGateway(
                                this->transmissionQueue.GetAt(0).ToTransmissionString()
                            );
                        }
                    }
                }
            }
        }
    }
}





