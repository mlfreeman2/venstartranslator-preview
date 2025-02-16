## About

### What is the Venstar Translator?
Venstar Translator is a small C# application that fetches temperature readings from semi-arbitrary HTTP URLs and translates them into the format necessary for Venstar ColorTouch sensors to think they're external temperature sources.

A single instance of this application can emulate up to 20 of Venstar's ACC-TSENWIFIPRO sensors.
20 is Venstar's own limit on sensor ID numbers, not a limit on the application end.

### How do I get started?
This application is meant to be run in Docker/Docker Compose. 
The temperature data protocol is UDP broadcast messages, so the application needs to be on the same VLAN/network as the target thermostat.

The first step would be to fill out a sensors.json file (see the sample file at [sensors.json.sample](VenstarTranslator/sensors.json.sample)).
The sample file has comments and is intentionally missing commas because it is not meant to be used as-is.

Once you have the sample file filled out, you need to deploy the application somewhere. 
It has only been tested in Docker Compose with host networking, with the physical Docker Compose host on the same VLAN as the thermostat. 
I will not provide network configuration support at all. 

This Docker Compose configuration block can be modified to get you up and running:
```
  venstartranslator:
    container_name: venstartranslator
    image: fake-docker-registry-server:5000/mlfreeman2/venstartranslator:latest
    restart: always
    volumes:
      - "./docker/venstartranslator/sensors.myhouse.json:/data/sensors.json"
    environment:
      TZ: "America/New_York"
      SensorFilePath: "/data/sensors.json"
      Kestrel__Endpoints__Http__Url: "http://*:42069"
    network_mode: host
    logging:
      options:
        max-size: "10m"
        max-file: "5"
```
You have to map the sensor file in to the container for the app to work.

You also have to set three things via environment variable: the time zone, the path to your sensor settings file, and the port to run the app on.

When the application starts, there are currently two user interfaces.

The first one is the stock Hangfire Dashboard at http://host:port/hangfire. 
This is how you can monitor to make sure sensor data packets are being broadcast properly/ 

The second one is at http://host:port/ui/. It is a very simple page that lists all sensors in your sensor.json file. It tells you whether a given one is enabled or not,  it allows you to test-fetch the current reading from your sensor source, and it allows you to send a pairing packet for when you actually want to add a sensor to your thermostat (you have to press a button on the front of the actual ACC-TSENWIFIPRO to make it send a pairing packet, this is the equivalent).


### Other
If you want to manually download Venstar thermostat firmware and poke around like I did, start at 
https://files.skyportlabs.com/ct1_firmware/venstar/firmware.json


