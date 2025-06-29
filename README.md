## About

### What is this app?
Venstar Translator is a small C# application that fetches temperature readings from arbitrary JSON endpoints and translates them into the format necessary for Venstar ColorTouch sensors to think they're Venstar's own external temperature sources.

A single instance of this application can emulate up to 20 of Venstar's ACC-TSENWIFIPRO sensors.
20 is Venstar's own limit on sensor ID numbers, not a limit on the application end.

### How do I get started?
This application is meant to be run in Docker Compose. 
The `docker-compose.yml.sample` file can be used to get you up and running in a docker compose environment.

The temperature data protocol is UDP broadcast messages, so the application needs to be on the same VLAN/network as the target thermostat.

It has only been tested in Docker Compose with host networking, with the physical Docker Compose host on the same VLAN as the thermostat. 


#### Steps:
1. In your Docker Compose file, map in a *file* path (not a directory) that the application can write a file to.   
The app will write important sensor details to a JSON file at this path automatically. This is the only file you need to back up.  
In the Docker Compose sample, Docker Compose will map a path named sensors.myhouse.json (in the same directory as the Docker Compose file) into the container at `/data/sensors.json`. 
2. Make sure that the environment variable `SensorFilePath` is set to the internal path you decided on in step 1.  
In my examples, it would have to be set to `/data/sensors.json` because that's where I mapped the outside path to in Docker Compose.
3. (OPTIONAL): If you have a huge house and you need to run more than one instance of this to handle more than 20 sensors, change `FakeMacPrefix`.  
The variable takes lowercase a-f and 0-9 (hex) and nothing else and it has to be 10 characters long. If you only run one instance of this app you can delete this (or leave it alone). If you need to change it, just change the last character from `8` to `7` or `9`. Deploying a second copy changed to `7` will give you 20 more sensors. Deploying a third copy changed to `9` will give you 20 more sensors on top of that, for a total of 60.
If you change this after setting things up, the thermostat will no longer recognize the sensors.
4. Become familiar with JSONPath and grab a sample response from your data source.  
See https://support.smartbear.com/alertsite/docs/monitors/api/endpoint/jsonpath.html to learn more about JSONPath.
4. Run the app. Open a browser to port 8080 (if you used the sample Docker Compose file) to see the UI for managing sensors.  
Click the "Test JSON Path" button in the upper right of the web UI to open a page where you can run a query against a JSON document to see if it's going to get what you want.  
Once you have a JSONPath that works, click "Add New Sensor" and fill out the fields to add your first sensor.
5. Once you have sensors set up (confirmed by clicking "Get Temperature" for each one and seeing the temperature you expect), click on "Send Pairing Packet" and walk over to your thermostat to finish setting the sensor up there.  
The thermostats hold on to pairing packets for 30-60 seconds, so you can click "Send Pairing Packet" for a couple of sensors at a time and you don't have to run.




### Other
If you want to manually download Venstar thermostat firmware and poke around like I did, start at 
https://files.skyportlabs.com/ct1_firmware/venstar/firmware.json


