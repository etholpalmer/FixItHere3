/// Great-circle distance.
///
/// Lived in two places: `ClientShared/Geo.fs` for the apps and a `private
/// haversineKm` in the backend's `Endpoints.fs`, because `Backend.Api`
/// references `Shared` but not `ClientShared`. Two identical implementations of
/// the same formula is a slow-motion drift, and the moment ETA became a shared
/// contract they had to be the same function.
module FixItHere.Shared.Geo

let distanceKm (lat1: float, lng1: float) (lat2: float, lng2: float) =
    let rad d = d * System.Math.PI / 180.0
    let dLat = rad (lat2 - lat1)
    let dLng = rad (lng2 - lng1)
    let a =
        sin (dLat / 2.0) ** 2.0
        + cos (rad lat1) * cos (rad lat2) * sin (dLng / 2.0) ** 2.0
    6371.0 * 2.0 * atan2 (sqrt a) (sqrt (1.0 - a))
