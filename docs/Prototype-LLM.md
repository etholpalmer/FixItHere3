
For a proof of concept, aggressively reduce the scope. The purpose of the demo is not to prove the business rules—it is to prove the experience.

⸻

# Demo Architecture

## Build only four projects.

```
FixItHere.Demo.sln
/src
    Backend.Api
    Customer.Mobile
    Provider.Mobile
    Shared
```

Skip everything else.

No Admin Portal.

No Customer Website.

No Provider Website.

No Event Sourcing.

No CQRS.

No Message Bus.

No Stripe.

No Authentication Server.

No Notifications.

No Email.

No SMS.

No Background Workers.

No Kubernetes.

No Microservices.

One ASP.NET Core backend.

One database.

Two MAUI apps.

That’s it.

⸻

# Technology

## Backend

ASP.NET Core Minimal APIs

F#

SQLite

Entity Framework Core

SignalR

JWT (fake authentication)

⸻

## Customer App

.NET MAUI

F#

⸻

## Provider App

.NET MAUI

F#

⸻

## Maps

Google Maps

or

OpenStreetMap

⸻

## Chat

SignalR

⸻

## Images

Store locally.

Don’t upload anything.

⸻

## Database

SQLite

Seed it automatically.

Every startup resets the demo data.

⸻

# Fake Authentication

Instead of registration.

Splash screen

↓

Choose Role

```
Customer
Provider
```

⸻

## Customer

Choose one

```
John
Mary
Steve
Susan
Bob
```

Press Login

Done.

⸻

## Provider

Choose

```
Mike's Plumbing
Joe Electric
Rapid Tire Repair
Elite HVAC
```

Done.

No passwords.

⸻

# Seed Database

Automatically create

20 customers

20 providers

50 completed jobs

30 pending jobs

Ratings

Pictures

Messages

Everything.

Every run starts with the exact same data.

⸻

# Customer Demo

Home

↓

Service Catalog

↓

Choose

> ```
> Plumbing
> Electrical
> Painting
> Mechanic
> Moving
> Cleaning
> ```

↓

Nearby Providers

↓

Select Provider

↓

Book

↓

Provider accepts

↓

Track Provider

↓

Chat

↓

Provider arrives

↓

Start Work

↓

Complete

↓

Fake Payment

↓

Rating

Done.

⸻

# Provider Demo

Home

↓

Available Jobs

↓

Accept

↓

Navigate

↓

Chat

↓

Arrived

↓

Start

↓

Complete

↓

Fake Payment

Done.

⸻

# Real GPS

Yes.

Absolutely.

When available.

⸻

Also add

### Developer Mode

```
Use Real GPS
or
Use Simulated GPS
```

⸻

### Simulated GPS

Map

Tap

Instantly move yourself there.

Or

### Search

```
Toronto
Mississauga
Brampton
```

Move there instantly.

⸻

Also

Move Along Route

A slider

```
0%
25%
50%
75%
100%
```

Moves the provider.

Great for demonstrations.

⸻

## Tracking

### Customer sees

Provider car

Estimated arrival

Distance

Moving icon

Provider picture

Provider name

Vehicle

Rating

Exactly like Uber.

Movement can simply interpolate between points every second.

⸻

Chat

One SignalR Hub.

Support

Text

Pictures

Typing…

Seen

Delivered

Time

⸻

### For demonstrations

Add

```
Auto Reply
```

Toggle

When ON

Messages come back automatically after

5 seconds.

Example

Customer

> Hi

Provider

> On my way.

Customer

(photo)

Provider

> Looks good.

> See you shortly.

⸻

## Fake Calls

Don’t integrate telephony.

Just display

```
Calling Mike...
```

After

10 seconds

```
Call Ended
```

Perfect.

⸻

## Fake Payments

No Stripe.

No cards.

Instead

Completion

↓

```
Payment Authorized
```

↓

Loading…

↓

```
Transferred to Provider
$85.00
```

Done.

Maybe animate a receipt.

⸻

## Fake Notifications

Just SignalR.

Popup

```
Provider Accepted
```

Popup

```
Provider Arriving
```

Popup

```
Payment Complete
```

No APNS.

No Firebase.

⸻

## Provider Availability

Simple switch

```
Online
Offline
```

When Online

Jobs appear.

⸻

## Fake Scheduling

No calendar.

Instead

```
Now
30 minutes
Tomorrow
Saturday
```

Enough.

⸻

## Ratings

One screen.

★★★★★

Comment

Done.

⸻

## Pictures

Allow

Camera

Gallery

Take up to five photos.

Store locally.

⸻

## Admin Mode

Hidden menu.

Allows

Reset Demo

Create Job

Move Provider

Complete Job

Cancel Job

Inject Messages

Change GPS

Force Payment

Create Customer

Create Provider

Populate Sample Data

⸻

## Demonstration Mode

This is probably the single biggest feature I’d build.

One button.

```
Start Demo
```

Everything happens automatically.

Customer books.

↓

Provider accepts.

↓

Provider moves.

↓

Chat starts.

↓

Provider arrives.

↓

Job starts.

↓

Payment.

↓

Rating.

Perfect for investors.

⸻

## Minimum Backend APIs

```
POST /login
GET /services
GET /providers
POST /jobs
GET /jobs
PUT /jobs/{id}/accept
PUT /jobs/{id}/arrive
PUT /jobs/{id}/start
PUT /jobs/{id}/complete
GET /messages
POST /messages
GET /ratings
POST /ratings
GET /location
PUT /location
POST /payment/simulate
```

Probably fewer than 20 endpoints.

⸻

## What to Skip Entirely

For the proof of concept, I would leave out:

* Identity providers (Google, Apple, Facebook)
* Stripe Connect integration
* Email and SMS
* Provider onboarding and KYC
* Disputes and appeals
* Event sourcing and CQRS
* NATS/JetStream
* Complex scheduling
* Background workers
* Admin website
* Analytics
* Push notifications
* Fraud detection
* Working hours
* Open Request board
* Travel fee logic
* Cancellation rules
* Multi-step estimate negotiation
* Production security hardening

⸻

## One extra feature worth including

Add a Demo Control Panel that’s available only in development mode. It would let you instantly switch between personas (customer or provider), reposition either party on the map, inject chat messages, force state transitions (accept, arrive, start, complete), simulate payment success or failure, and reset the entire database with one click. This turns the proof of concept into a polished demonstration tool, making it easy to show every major workflow without relying on timing or manual setup.
