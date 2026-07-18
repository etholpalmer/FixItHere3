module FixItHere.Customer.Location

open System.Threading.Tasks
open Microsoft.Maui.Devices.Sensors

/// Best-effort GPS: returns fallback on permission denial, timeout, or any failure.
let getCurrent (fallback: float * float) : Task<float * float> =
    task {
        try
            let! loc = Geolocation.Default.GetLocationAsync(GeolocationRequest(GeolocationAccuracy.Medium))
            if isNull (box loc) then return fallback
            else return (loc.Latitude, loc.Longitude)
        with _ -> return fallback
    }
