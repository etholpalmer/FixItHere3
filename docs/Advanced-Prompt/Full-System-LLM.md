
This is an excellent foundation. I read through your specification carefully. It is much more than an app idea—it is already the beginning of a ubiquitous language and domain model suitable for Domain-Driven Design (DDD). [PlanTheApp.md](./PlanTheApp.md)

If your goal is to build this with an LLM, I would not ask it to “build an app.” I would ask it to act as the Chief Software Architect and produce a complete production-quality system in phases.

Considering your preferences over the past several months (F#, event sourcing, DSLs, pipelines, SurrealDB, NATS, TigerBeetle, property testing, diagnostics, etc.), I would actually have the LLM generate an entire platform rather than just two mobile apps.

I would have it produce:

* Customer Mobile App
* Provider Mobile App
* Customer Website
* Provider Website
* Admin Website
* Backend APIs
* Identity Service
* Payments Service
* Scheduling Service
* Messaging Service
* Notification Service
* Search & Geo Service
* Event Store
* Read Models
* CI/CD
* Infrastructure
* Testing
* Documentation

all from one architectural specification.

⸻

# The Prompt

I would make the prompt something like the following.

⸻

# ROLE

You are a Principal Software Architect, UX Designer, Domain Driven Design expert, Event Sourcing expert, F# architect, C# architect, .NET Architect, Mobile Architect, Cloud Architect, DevOps Engineer and Technical Writer.

You are designing a complete commercial marketplace platform similar in quality to Uber, DoorDash, Airbnb and TaskRabbit.

The objective is not to generate code immediately.

The objective is to design the entire platform so that implementation can begin with almost no ambiguity.

Assume this system will eventually support millions of users.

Design for scalability from Day One.

⸻

# INPUT

The attached markdown document is the Domain Language and Business Specification.

Treat every definition as the canonical ubiquitous language.

Do not invent alternative terminology.

Use those exact terms throughout every design document.

⸻

# PRIMARY GOAL

Produce a complete implementation specification for building:

1. Customer Mobile Application
2. Provider Mobile Application
3. Customer Web Site
4. Provider Web Site
5. Administration Portal
6. Backend Services
7. Infrastructure
8. APIs
9. Deployment
10. Testing
11. Documentation

⸻

# TECHNOLOGY STACK

Unless impossible, prefer:

## Frontend

• .NET MAUI

• F#

• C#

## Backend

• ASP.NET Core

• Minimal APIs

• F#

• C#

## Domain Layer

Prefer F#

### Infrastructure

C#

## Persistence

Initially PostgreSQL

Later support

SurrealDB

SQL Server

SQLite

CosmosDB

## Storage must be abstracted.

Messaging

NATS

JetStream

## Event Store

Event Sourcing

CQRS

Projection pipelines

Property based testing

## Diagnostics

OpenTelemetry

Prometheus

GreyLog

Serilog

Log4Net adapters

## Configuration

Strongly typed

Dependency Injection

## Secret Management

Bitwarden

Hashicorp Vault

## Authentication

Microsoft Identity

Google

Apple

Facebook

Email/password

Magic links

Passkeys

## Payment

Stripe Connect Express

## Maps

Google Maps

Apple Maps

OpenStreetMap abstraction

## Push Notifications

Firebase

Apple Push Notification Service

SMS

Twilio abstraction

Email

SendGrid abstraction

## Images

Azure Blob Storage abstraction

## Testing

xUnit

FsCheck

Deterministic Simulation Testing

Property Based Testing

Integration Tests

Performance Tests

Load Tests

Chaos Tests

⸻

# OUTPUT

Produce documentation only.

Do not produce implementation until requested.

⸻

## Produce the following documents.

⸻

### 1 Executive Summary

Explain

Business goals

Vision

Target audience

Competitive advantages

Success metrics

⸻

### 2 Domain Driven Design

Bounded Contexts

Aggregates

Entities

Value Objects

Events

Commands

Queries

Policies

Specifications

Repositories

Factories

Domain Services

Anti Corruption Layers

Context Maps

⸻

### 3 Architecture

Explain

Modular Monolith vs Microservices

Justify recommendation

Include migration strategy

⸻

### 4 Solution Layout

Generate an entire Visual Studio solution.

Example

/src

/domain

/application

/infrastructure

/mobile.customer

/mobile.provider

/web.customer

/web.provider

/admin.portal

/gateway

/services

/shared

/tests

/docs

/tools

/scripts

⸻

### 5 Mobile Apps

Design both apps separately.

For each app include

Navigation

Wireframes

User flows

Offline behavior

Synchronization

Accessibility

Dark mode

Localization

Permissions

Security

Battery optimization

Performance

Animations

⸻

### 6 Customer App

Every screen

Every dialog

Every workflow

Every state

Every validation

⸻

### 7 Provider App

Include

Working Hours

Online Status

Scheduling

Open Request Board

Claims

Messaging

Navigation

Maps

Ratings

Payments

Profile

Identity Verification

Availability

Background location

GPS

⸻

### 8 Admin Portal

Everything needed to operate the marketplace.

Users

Providers

Jobs

Disputes

Payments

Refunds

Appeals

Reports

Metrics

Notifications

CMS

Feature Flags

Support

Audit Logs

Impersonation

⸻

### 9 API Design

REST

OpenAPI

Versioning

Error Handling

Problem Details

Pagination

Filtering

Sorting

Searching

Caching

Rate Limiting

Idempotency

Authentication

Authorization

⸻

### 10 Database Design

ER diagrams

Indexes

Constraints

Partitioning

Soft Deletes

Auditing

Multi-tenancy strategy

⸻

### 11 Event Sourcing Design

Commands

Events

Snapshots

Versioning

Replay

Projections

Read Models

Consistency

Compensation

Sagas

⸻

### 12 Messaging

NATS

JetStream

Topics

Consumers

Retry

Dead Letter Queues

Ordering

Idempotency

Exactly Once strategy

⸻

### 13 Payments

Stripe Connect

Authorizations

Captures

Refunds

Partial Refunds

Travel Fee

Provider payouts

Disputes

Taxes

Accounting

⸻

### 14 Mapping

GPS

Tracking

Geofencing

ETA

Distance calculations

Search Radius

Routing abstraction

⸻

### 15 Security

OWASP

Threat Modeling

Encryption

Secrets

PII

Fraud Detection

Rate Limiting

Audit

Device Trust

Passkeys

JWT

Refresh Tokens

⸻

### 16 Diagnostics

Health Checks

Distributed Tracing

Metrics

Logging

Correlation IDs

Recovery Dashboard

GreyLog

Prometheus

OpenTelemetry

⸻

### 17 Testing

Unit Tests

Property Tests

Deterministic Simulation Tests

UI Tests

Load Tests

Chaos Tests

Security Tests

Performance Tests

⸻

### 18 CI/CD

GitHub Actions

Azure DevOps

Docker

Containers

Deployment

Rollback

Database Migration

Blue Green Deployment

Canary Releases

⸻

### 19 Coding Standards

F# Guidelines

C# Guidelines

DDD Rules

Architecture Decision Records

Code Reviews

Naming Standards

Folder Standards

⸻

### 20 Future Roadmap

Design the architecture so that future releases can add

AI dispatch

AI pricing

Subscriptions

Advertising

Enterprise Providers

Provider Teams

Fleet Management

Customer Memberships

Coupons

Gift Cards

Dynamic Pricing

Internationalization

Multiple Currencies

Multiple Languages

Marketplace Analytics

Machine Learning

⸻

## IMPORTANT

Never violate the attached domain language.

Treat it as the single source of truth.

Whenever terminology conflicts with common marketplace terminology, always prefer the attached glossary.

Never rename concepts.

Never merge concepts.

Never simplify the domain.

Always explain trade-offs before making architectural decisions.

Whenever assumptions are necessary, explicitly list them.

When multiple valid solutions exist, compare them and recommend one with clear justification.

⸻

One suggestion that would improve this even further

Given the quality of your domain glossary, I’d actually split the work into three separate LLM prompts rather than one:

1. Architecture Prompt (300–500 pages of specifications)
2. UX/UI Prompt (wireframes, user journeys, design system, accessibility, every screen)
3. Implementation Prompt (generate the actual F#/C# solution from the approved architecture)

This staged approach usually produces a significantly better result than asking an LLM to design and implement everything in one pass, because it allows you to review and refine the architecture before code generation begins. It also aligns well with the structured, DSL-driven development style you’ve been using in your other .NET and F# projects.
