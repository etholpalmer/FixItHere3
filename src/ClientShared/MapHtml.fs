module FixItHere.ClientShared.MapHtml

/// Self-contained Leaflet page: customer pin fixed, provider car marker driven by
/// the page's own SignalR connection (mirrors the /dev console pattern).
///
/// NOTE: this is an F# format string. Every literal percent sign in the CSS or JS
/// below must be written as `%%`, or the printf type-checker will read it as a
/// placeholder. The four real placeholders are, in order: jobLat, jobLng,
/// providerId, baseUrl.
///
/// Unlike the /dev console — a dark operator instrument — this map renders inside
/// the consumer app, so it stays light and legible against the product's own
/// surface. The amber provider marker is the shared brand thread between them.
let render (baseUrl: string) (jobLat: float) (jobLng: float) (providerId: int) : string =
    sprintf """<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css">
<style>
  :root {
    --brand: oklch(0.72 0.15 80);
    --ink: oklch(0.22 0.01 85);
    --ring: oklch(1 0 0);
  }
  html, body, #map { margin: 0; height: 100%%; }
  #map { background: oklch(0.96 0.002 85); }
  .leaflet-container { font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
  .leaflet-control-attribution { font-size: 10px; opacity: 0.7; }

  /* Destination: dark dot, white ring — a fixed reference point. */
  .dest {
    width: 16px; height: 16px;
    border-radius: 50%%;
    background: var(--ink);
    border: 3px solid var(--ring);
    box-shadow: 0 1px 5px oklch(0.2 0 0 / 0.4);
  }

  /* Provider: brand amber, with a halo that breathes so the eye finds the
     moving marker without it stealing focus from the route. */
  .car {
    position: relative;
    width: 18px; height: 18px;
    border-radius: 50%%;
    background: var(--brand);
    border: 3px solid var(--ring);
    box-shadow: 0 2px 8px oklch(0.2 0 0 / 0.45);
  }
  .car::after {
    content: "";
    position: absolute;
    inset: -9px;
    border-radius: 50%%;
    border: 2px solid var(--brand);
    opacity: 0;
    animation: halo 2.2s cubic-bezier(0.22, 1, 0.36, 1) infinite;
  }
  @keyframes halo {
    0%%   { transform: scale(0.55); opacity: 0.75; }
    100%% { transform: scale(1.25); opacity: 0; }
  }
  @media (prefers-reduced-motion: reduce) {
    .car::after { animation: none; opacity: 0.4; transform: scale(1); }
  }
</style></head>
<body><div id="map"></div>
<script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
<script src="https://unpkg.com/@microsoft/signalr@8.0.0/dist/browser/signalr.min.js"></script>
<script>
const jobPos = [%f, %f], providerId = %d, baseUrl = "%s";
const map = L.map("map", { zoomControl: false, attributionControl: true }).setView(jobPos, 12);
L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", { maxZoom: 19 }).addTo(map);

const destIcon = L.divIcon({ className: "", html: '<div class="dest"></div>', iconSize: [16, 16], iconAnchor: [8, 8] });
const carIcon  = L.divIcon({ className: "", html: '<div class="car"></div>',  iconSize: [18, 18], iconAnchor: [9, 9] });

L.marker(jobPos, { icon: destIcon }).addTo(map).bindPopup("You");
const car = L.marker(jobPos, { icon: carIcon }).addTo(map);

const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
let target = null, current = null;
function step() {
  if (target) {
    current = current || target;
    current = [current[0] + (target[0]-current[0]) * 0.2, current[1] + (target[1]-current[1]) * 0.2];
    car.setLatLng(current);
  }
  requestAnimationFrame(step);
}
if (reduce) {
  // Snap instead of easing; no continuous rAF loop.
  car.setLatLng(jobPos);
} else {
  step();
}

const conn = new signalR.HubConnectionBuilder().withUrl(baseUrl + "/hub").withAutomaticReconnect().build();
conn.on("LocationUpdated", l => {
  if (l.providerId !== providerId) return;
  if (reduce) { car.setLatLng([l.lat, l.lng]); } else { target = [l.lat, l.lng]; }
});
conn.start();
setTimeout(() => map.invalidateSize(), 400);
</script></body></html>""" jobLat jobLng providerId baseUrl
