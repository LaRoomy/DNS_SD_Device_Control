#include <Arduino.h>
#include <WiFi.h>
#include <ESPmDNS.h>

#include "TransmissionControl.h"

#define HBUTTON_1 18
#define LED_RED 4
#define LED_GREEN 5

char ssid[] = "<enter network name here>";          // network SSID (name)
char pass[] = "<enter network password here";       // network password

// Initialize the client library
WiFiClient client;

// timer params
long mTimer = 0;
long mTimeOut = 2000;

// control params
bool mdns_query_success = false;
unsigned int dataOutputCounter = 0;

// TransmissionController event handler class
class TransmissionControllerEventHandler : public ITransmissionControlInterface
{
public:
    void OutGateway(const String& data) override
    {
        if(client.connected()){
            client.println(data);
        }
    }
    void OnDataDecoded(const String& data) override
    {
        Serial.print("Received data decoded: ");
        Serial.println(data);
    }
    void OnUnencryptedDataReceived(const String& data) override
    {
        Serial.print("Received unencrypted data: ");
        Serial.println(data);
    }
};

// Global TransmissionController instance
TransmissionControl* transmissionController = nullptr;

void setup() {

    pinMode(HBUTTON_1, INPUT);
    pinMode(LED_RED, OUTPUT);
    pinMode(LED_GREEN, OUTPUT);

    digitalWrite(LED_RED, LOW);
    digitalWrite(LED_GREEN, LOW);

    Serial.begin(115200);
    Serial.print("Attempting to connect to Network...");

    WiFi.begin(ssid, pass);

    while ( WiFi.status() != WL_CONNECTED) {
        Serial.print(".");
        digitalWrite(LED_RED, digitalRead(LED_RED) == LOW ? HIGH : LOW);
        delay(400);
    }

    Serial.println("Wifi connection established!");
    Serial.print("Local IP Address: ");
    Serial.println(WiFi.localIP());

    digitalWrite(LED_RED, HIGH);

    transmissionController = new TransmissionControl();
    if(transmissionController != nullptr)
    {
        transmissionController->SetInterface(
            dynamic_cast<ITransmissionControlInterface*>(new TransmissionControllerEventHandler()
        ));
        transmissionController->SetDeviceName("esp32-AnotherDevice");
    }

    if(mdns_init() != ESP_OK){
        Serial.println("Error: mdns initialization error!");
    }
    else {
        auto numServices =
            MDNS.queryService("_mydevices", "_tcp");

        if(numServices == 0){
            Serial.println("Warning: No mdns services were found.");
        }
        else {
            mdns_query_success = true;

            Serial.print("Number of services found:  ");
            Serial.println(numServices);

            for(unsigned int i = 0; i < numServices; i++){
                Serial.print("Information of Service with index: ");
                Serial.println(i);
                Serial.print("Hostname: ");
                Serial.println(MDNS.hostname(i));
                Serial.print("IP Address: ");
                Serial.println(MDNS.IP(i));
                Serial.print("Port: ");
                Serial.println(MDNS.port(i));
            }
            Serial.print("\r\n");
            Serial.println("Trying to connect to host with index 0..");

            if(!client.connect(MDNS.IP(0), MDNS.port(0))){
                Serial.println("Client connection FAILED!");
            } else {
                Serial.println("Client connection SUCCEEDED!");
                digitalWrite(LED_GREEN, HIGH);
            }
        }
    }
    mTimer = millis();
}

void loop() {

    if(transmissionController != nullptr)
    {
        transmissionController->OnLoop();
    }

    if(millis() > (unsigned long)(mTimeOut + mTimer)) {
        mTimer = millis();

        if(client.connected())
        {
            if(transmissionController != nullptr)
            {
                transmissionController->OnClientConnected();
            }
        }
        else
        {
            if(transmissionController != nullptr)
            {
                transmissionController->OnClientDisconnected();
            }

            // check if there is a service provided by the server
            if(mdns_query_success == false)
            {
                Serial.println("No successful MDNS query - trying again");

                auto numServices =
                    MDNS.queryService("_mydevices", "_tcp");

                if(numServices != 0){
                    mdns_query_success = true;
                }
            }
            else
            {
                digitalWrite(LED_GREEN, LOW);
                
                // try to connect to the server
                if(!client.connect(MDNS.IP(0), MDNS.port(0)))
                {
                    Serial.println("Client connection FAILED!");
                }
                else
                {
                    Serial.println("Client connection SUCCEEDED!");
                    digitalWrite(LED_GREEN, HIGH);
                }
            }
        }
    }

    if(client.available()){

        String data;

        while(1){   // read data from client
            auto c = client.read();
            if(c == -1){
                break;
            }
            else {
                data += (char)c;
            }
        }

        //Serial.print("Data received: ");
        //Serial.println(data);

        if(transmissionController != nullptr)
        {
            transmissionController->OnDataReceived(data);
        }
    }

    // check if the hardware-button is pressed
    if(digitalRead(HBUTTON_1) == LOW){
        delay(20);

        //Serial.println("Button 1 pressed!");

        if(transmissionController != nullptr)
        {
            if(dataOutputCounter == 0)
            {
                transmissionController->SendData("This is a message from the remote device to the dns-sd server. This message was sent with end to end encryption.", true);
                dataOutputCounter++;
            }
            else if(dataOutputCounter == 1)
            {
                transmissionController->SendData("This is a another message to the dns-sd server. But this message was sent in plain text..!", false);
                dataOutputCounter++;
            }
            else
            {
                transmissionController->SendData("This is the third message. Now it starts over.", true);
                dataOutputCounter = 0;
            }
        }

        while(digitalRead(HBUTTON_1) == LOW);

        delay(20);
    }
}
