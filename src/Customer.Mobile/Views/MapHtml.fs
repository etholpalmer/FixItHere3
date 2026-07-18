module FixItHere.Customer.Views.MapHtml

open FixItHere.Customer

/// Self-contained Leaflet page: customer pin fixed, provider car marker driven by
/// the page's own SignalR connection (mirrors the /dev console pattern).
let render (jobLat: float) (jobLng: float) (providerId: int) : string =
    sprintf """<!doctype html><html><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css">
<style>html,body,#map{margin:0;height:100%%;}</style></head>
<body><div id="map"></div>
<script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
<script src="https://unpkg.com/@microsoft/signalr@8.0.0/dist/browser/signalr.min.js"></script>
<script>
const jobPos = [%f, %f], providerId = %d, baseUrl = "%s";
const map = L.map("map").setView(jobPos, 12);
L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png").addTo(map);
L.marker(jobPos).addTo(map).bindPopup("You");
const car = L.circleMarker(jobPos, { radius: 9, color: "#1565c0", fillOpacity: 0.9 }).addTo(map);
let target = null, current = null;
function step() {
  if (target) {
    current = current || target;
    current = [current[0] + (target[0]-current[0]) * 0.2, current[1] + (target[1]-current[1]) * 0.2];
    car.setLatLng(current);
  }
  requestAnimationFrame(step);
}
step();
const conn = new signalR.HubConnectionBuilder().withUrl(baseUrl + "/hub").withAutomaticReconnect().build();
conn.on("LocationUpdated", l => { if (l.providerId === providerId) target = [l.lat, l.lng]; });
conn.start();
setTimeout(() => map.invalidateSize(), 400);
</script></body></html>""" jobLat jobLng providerId Config.baseUrl
