module FixItHere.Shared.Places

/// A real GTA address with its coordinates.
///
/// This replaces a random bounding box. The seed used to draw coordinates from
/// [43.55, 43.85] x [-79.75, -79.20] uniformly — roughly 15-18% of which is open
/// water, so a customer, and therefore their job, could land in Lake Ontario.
/// On a map-driven demo that is visible in the first second.
///
/// A curated list also removes the need for any point-in-polygon machinery: if
/// every coordinate comes from here, none *can* be in water. The one test worth
/// having asserts exactly that — seeded coordinates are drawn from this set —
/// rather than re-deriving a shoreline.
///
/// Every entry is deliberately inland. Lakeside neighbourhoods (Port Credit,
/// Long Branch, the Toronto Islands) are excluded rather than nudged, because a
/// pin a block from the shore invites the question the list exists to avoid.
type Place =
    { Address: string
      Neighbourhood: string
      Lat: float
      Lng: float }

/// Ordered, and order is part of the contract: the seed indexes into this list,
/// so inserting in the middle reshuffles which customer lives where. Append.
let all : Place list =
    [ // --- Toronto ---
      { Address = "501 Bloor St W";        Neighbourhood = "The Annex";           Lat = 43.6650; Lng = -79.4103 }
      { Address = "120 Yorkville Ave";     Neighbourhood = "Yorkville";           Lat = 43.6712; Lng = -79.3927 }
      { Address = "88 Nassau St";          Neighbourhood = "Kensington Market";   Lat = 43.6551; Lng = -79.4021 }
      { Address = "620 College St";        Neighbourhood = "Little Italy";        Lat = 43.6546; Lng = -79.4200 }
      { Address = "210 Roncesvalles Ave";  Neighbourhood = "Roncesvalles";        Lat = 43.6472; Lng = -79.4498 }
      { Address = "1910 Bloor St W";       Neighbourhood = "High Park";           Lat = 43.6534; Lng = -79.4665 }
      { Address = "2960 Dundas St W";      Neighbourhood = "The Junction";        Lat = 43.6656; Lng = -79.4713 }
      { Address = "2301 Bloor St W";       Neighbourhood = "Bloor West Village";  Lat = 43.6503; Lng = -79.4841 }
      { Address = "1071 Queen St E";       Neighbourhood = "Leslieville";         Lat = 43.6635; Lng = -79.3312 }
      { Address = "740 Broadview Ave";     Neighbourhood = "Riverdale";           Lat = 43.6742; Lng = -79.3543 }
      { Address = "1938 Queen St E";       Neighbourhood = "The Beaches";         Lat = 43.6707; Lng = -79.2966 }
      { Address = "442 Danforth Ave";      Neighbourhood = "Greektown";           Lat = 43.6779; Lng = -79.3520 }
      { Address = "2300 Yonge St";         Neighbourhood = "Yonge & Eglinton";    Lat = 43.7066; Lng = -79.3985 }
      { Address = "380 Spadina Rd";        Neighbourhood = "Forest Hill";         Lat = 43.6959; Lng = -79.4131 }
      { Address = "3080 Yonge St";         Neighbourhood = "Lawrence Park";       Lat = 43.7266; Lng = -79.4025 }
      { Address = "5150 Yonge St";         Neighbourhood = "North York Centre";   Lat = 43.7683; Lng = -79.4127 }
      { Address = "1090 Don Mills Rd";     Neighbourhood = "Don Mills";           Lat = 43.7346; Lng = -79.3457 }
      { Address = "300 Borough Dr";        Neighbourhood = "Scarborough Centre";  Lat = 43.7757; Lng = -79.2578 }
      { Address = "4141 Sheppard Ave E";   Neighbourhood = "Agincourt";           Lat = 43.7846; Lng = -79.2823 }
      { Address = "3300 Bloor St W";       Neighbourhood = "Islington";           Lat = 43.6452; Lng = -79.5232 }
      { Address = "2444 Eglinton Ave E";   Neighbourhood = "Kennedy Park";        Lat = 43.7307; Lng = -79.2610 }
      { Address = "1500 Royal York Rd";    Neighbourhood = "Humber Heights";      Lat = 43.6889; Lng = -79.5147 }
      { Address = "2150 Lawrence Ave E";   Neighbourhood = "Wexford";             Lat = 43.7527; Lng = -79.2896 }
      { Address = "1240 St Clair Ave W";   Neighbourhood = "Corso Italia";        Lat = 43.6790; Lng = -79.4482 }
      // --- 905 ---
      { Address = "100 City Centre Dr";    Neighbourhood = "Mississauga";         Lat = 43.5931; Lng = -79.6425 }
      { Address = "3050 Confederation Pkwy"; Neighbourhood = "Cooksville";        Lat = 43.5793; Lng = -79.6224 }
      { Address = "8500 Torbram Rd";       Neighbourhood = "Brampton";            Lat = 43.7284; Lng = -79.7106 }
      { Address = "25 Peel Centre Dr";     Neighbourhood = "Bramalea";            Lat = 43.7182; Lng = -79.7205 }
      { Address = "3120 Highway 7";        Neighbourhood = "Vaughan";             Lat = 43.7942; Lng = -79.5273 }
      { Address = "9200 Bathurst St";      Neighbourhood = "Thornhill";           Lat = 43.8137; Lng = -79.4527 }
      { Address = "8360 Kennedy Rd";       Neighbourhood = "Markham";             Lat = 43.8561; Lng = -79.3382 }
      { Address = "10720 Yonge St";        Neighbourhood = "Richmond Hill";       Lat = 43.8828; Lng = -79.4403 }
      { Address = "7955 Islington Ave";    Neighbourhood = "Woodbridge";          Lat = 43.7825; Lng = -79.5972 }
      { Address = "2900 Rutherford Rd";    Neighbourhood = "Maple";               Lat = 43.8398; Lng = -79.5093 }
      { Address = "1355 Kingston Rd";      Neighbourhood = "Pickering";           Lat = 43.8339; Lng = -79.0870 }
      { Address = "250 Bayly St W";        Neighbourhood = "Ajax";                Lat = 43.8501; Lng = -79.0348 }
      { Address = "4100 Kingston Rd";      Neighbourhood = "Highland Creek";      Lat = 43.7826; Lng = -79.1758 }
      { Address = "1801 Dundas St E";      Neighbourhood = "Whitby";              Lat = 43.8785; Lng = -78.9268 }
      { Address = "6600 Steeles Ave E";    Neighbourhood = "Milliken";            Lat = 43.8271; Lng = -79.2662 }
      { Address = "500 Rexdale Blvd";      Neighbourhood = "Rexdale";             Lat = 43.7160; Lng = -79.5842 } ]

let count = List.length all

/// Deterministic pick. Wraps, so callers need not know the list length.
let at (index: int) = all.[((index % count) + count) % count]

/// "501 Bloor St W, The Annex" — what a job shows the provider.
let fullAddress (p: Place) = sprintf "%s, %s" p.Address p.Neighbourhood

/// True when a coordinate came from this list. The seed's only geographic
/// assertion: cheap, exact, and it cannot drift from the data the way a
/// hand-maintained shoreline polygon would.
let isKnownPlace (lat: float) (lng: float) =
    all |> List.exists (fun p -> abs (p.Lat - lat) < 1e-9 && abs (p.Lng - lng) < 1e-9)
