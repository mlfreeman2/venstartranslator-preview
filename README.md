## About

### What is "Venstar Translator"?
Venstar Translator is a small C# application that fetches temperature readings from arbitrary JSON endpoints and translates them into the format necessary for Venstar ColorTouch sensors to think they're Venstar's own external temperature sources.

A single instance of this application can emulate up to 20 of Venstar's ACC-TSENWIFIPRO sensors.
20 is Venstar's own limit on sensor ID numbers, not a limit on the application end.

### How do I get started?
This application is meant to be run in Docker/Docker Compose. 
The temperature data protocol is UDP broadcast messages, so the application needs to be on the same VLAN/network as the target thermostat.

It has only been tested in Docker Compose with host networking, with the physical Docker Compose host on the same VLAN as the thermostat. 
I will not provide network configuration support at all. 

The `docker-compose.yml.sample` file can be used to get you up and running in a docker compose environment.

If you don't use Docker Compose, you can try this ChatGPT translation to a plain `docker` command (not tested, I use Docker Compose):
```
docker run -d \
  --name venstartranslator \
  --network host \
  --restart always \
  -v "$PWD/sensors.myhouse.json:/data/sensors.json" \
  -e TZ='America/New_York' \
  -e SensorFilePath='/data/sensors.json' \
  -e Kestrel__Endpoints__Http__Url='http://*:8080' \
  -e FakeMacPrefix='428e0486d8' \
  --log-opt max-size=10m \
  --log-opt max-file=5 \
  ghcr.io/mlfreeman2/venstartranslator-preview:main
```

#### Steps:
1. Provide a path that the application can write a JSON file to. Map it into your container. The important sensor details get backed up to a JSON file automatically. You need to give a full file path, not just a folder. In the Docker Compose sample, Docker Compose will map a path named sensors.myhouse.json (in the same directory as the Docker Compose file) into the container at `/data/sensors.json`. 
2. Make sure that the environment variable `SensorFilePath` is set to the internal path you decided on in step 1. In my examples, it would have to be set to `/data/sensors.json` because that's where I mapped the outside path to in Docker Compose.
3. (OPTIONAL): If you have a huge house and you need to run more than one instance of this to handle more than 20 sensors, change `FakeMacPrefix`. The variable takes lowercase a-f and 0-9 (hex) and nothing else and it has to be 10 characters long. If you only run one instance of this app you can delete this (or leave it alone).
4. Run the app. Open a browser to http://something:8080/ui/ to see the UI for setting up sensors.
5. Once you have sensors set up (confirmed by clicking "Get Temperature" and seeing the temperature you expect), click on "Send Pairing Packet" and walk over to your thermostat to finish setting the sensor up there. The thermostats hold on to pairing packets for 30-60 seconds, so you don't have to run.




### Other
If you want to manually download Venstar thermostat firmware and poke around like I did, start at 
https://files.skyportlabs.com/ct1_firmware/venstar/firmware.json


