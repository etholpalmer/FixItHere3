/// Kept as a one-line forwarder so existing call sites in both apps stay put.
/// The implementation moved to `Shared` when the backend needed the same
/// formula for ETA — see FixItHere.Shared.Geo.
module FixItHere.ClientShared.Geo

let distanceKm = FixItHere.Shared.Geo.distanceKm
