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
    --dest: oklch(0.55 0.17 255);
    --ring: oklch(1 0 0);
  }
  html, body, #map { margin: 0; height: 100%%; }
  #map { background: oklch(0.96 0.002 85); }
  .leaflet-container { font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
  .leaflet-control-attribution { font-size: 10px; opacity: 0.7; }

  /* Destination: blue, the convention for "you are here" on every map app the
     audience has ever used. Dark-on-white read as a generic pin and, worse, sat
     invisible under the amber provider marker whenever the two coincided. */
  .dest {
    width: 16px; height: 16px;
    border-radius: 50%%;
    background: var(--dest);
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

const dest = L.marker(jobPos, { icon: destIcon }).addTo(map).bindPopup("You");

/* The provider marker is created but NOT added until a real position arrives.
   It used to be initialised at jobPos, so before the first LocationUpdated both
   markers occupied the same coordinate and the amber one painted over the blue
   — the map showed a single dot and looked like it had lost the customer. */
const car = L.marker(jobPos, { icon: carIcon });
let carPlaced = false;

/* Keep both dots in frame, and close in as they converge — the visual the whole
   tracking screen exists to deliver. maxZoom stops a provider who is nearly
   there from slamming the view to street level. */
function frame() {
  if (!carPlaced) { map.setView(jobPos, 13); return; }
  map.fitBounds(L.latLngBounds([jobPos, car.getLatLng()]).pad(0.35),
                { maxZoom: 16, animate: true, duration: 0.6 });
}

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
if (!reduce) step();

/* withCredentials:false is load-bearing, not a nicety. This page is loaded as an
   HTML string, so its origin is null and every hub request is cross-origin. The
   server's CORS is AllowAnyOrigin (no credentials) — which the browser refuses to
   pair with a credentialed request. SignalR's client defaults withCredentials to
   true, so its negotiate was silently blocked and the socket never opened, while
   the plain `fetch` below (no credentials by default) succeeded — which is why the
   car was placed once and then never tracked. */
const conn = new signalR.HubConnectionBuilder()
    .withUrl(baseUrl + "/hub", { withCredentials: false })
    .withAutomaticReconnect().build();
function place(lat, lng) {
  if (!carPlaced) {
    car.setLatLng([lat, lng]).addTo(map);
    current = [lat, lng];
    carPlaced = true;
  }
  if (reduce) { car.setLatLng([lat, lng]); } else { target = [lat, lng]; }
  frame();
}

/* Read both casings. SignalR's JSON hub protocol serialises the DTO PascalCase
   (ProviderId/Lat/Lng, matching the F# record the typed clients decode), while
   the REST /location fetch above is camelCase. Reading only camelCase here meant
   every live push failed the id guard and was silently dropped — the fetch
   placed the car once and it never moved again, so the map never tracked the
   drive or closed in. */
conn.on("LocationUpdated", l => {
  const pid = l.providerId ?? l.ProviderId;
  const lat = l.lat ?? l.Lat, lng = l.lng ?? l.Lng;
  if (pid !== providerId) return;
  place(lat, lng);
});
conn.start();

/* Ask once on load rather than waiting for the provider to move. Without this
   the map shows no provider at all until the next push, which on a Scheduled
   job may be minutes away — the screen reads as broken rather than as waiting. */
fetch(baseUrl + "/location?providerId=" + providerId)
  .then(r => r.json())
  .then(env => { if (env && env.success && env.data) place(env.data.lat, env.data.lng); })
  .catch(() => {});

setTimeout(() => { map.invalidateSize(); frame(); }, 400);
</script></body></html>""" jobLat jobLng providerId baseUrl
