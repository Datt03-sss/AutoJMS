# AutoJMS Documentation

## Overview

AutoJMS is a desktop logistics automation application for Vietnamese logistics operations.

This folder is for developer documentation: audit, architecture, manual operations, roadmap, troubleshooting, and release notes. Documentation may propose code/script changes, but those changes must be done in a separate explicit task.

## Structure

```
docs/
├── README.md                          ← You are here
├── START_HERE.md                      ← First-read guide
├── IMPLEMENTATION_PLAN.md             ← Safe implementation order
├── WORKFLOW_SUMMARY.md                ← Short workflow reference
├── VIBE_CODING_GUIDE.md               ← AI/vibe coding guardrails
├── architecture/                      ← Architecture documentation
│   ├── system-overview.md
│   ├── client-architecture.md
│   ├── backend-architecture.md
│   ├── tier-architecture.md
│   ├── auth-token-architecture.md
│   └── fullstack-operation-architecture.md
├── api/                              ← API documentation
│   ├── render-server-api.md
│   ├── firebase-license-schema.md
│   ├── jms-api-notes.md
│   └── supabase-manifest-schema.md
├── release/                           ← Release documentation
│   ├── release-overview.md
│   ├── inno-first-install.md
│   ├── velopack-update.md
│   ├── github-release-flow.md
│   ├── supabase-manifest-flow.md
│   └── versioning-rules.md
├── manual/                            ← Practical manual operations
│   ├── MANUAL_OPERATIONS.md
│   └── QUICK_RELEASE_CHECKLIST.md
├── dev/                               ← Development workflow and codebase index
│   ├── DEVELOPMENT_WORKFLOW.md
│   ├── CODING_STANDARDS.md
│   ├── CODEBASE_INDEX.md
│   ├── FEATURE_MODULE_MAP.md
│   ├── RISK_REGISTER.md
│   └── ONBOARDING.md
├── roadmap/                           ← Phase roadmap and backlog
│   ├── DEVELOPMENT_ROADMAP.md
│   ├── PRIORITY_TASKS.md
│   ├── SAFE_REFACTOR_PLAN.md
│   ├── BUILD_STABILIZATION_PLAN.md
│   ├── RELEASE_STABILIZATION_PLAN.md
│   ├── TIER_BASE_ULTRA_PLAN.md
│   └── FULLSTACK_OPERATION_PLAN.md
├── migration/                         ← Migration guides
│   ├── current-to-clean-structure-plan.md
│   ├── config-migration-plan.md
│   └── background-job-tier-policy-plan.md
├── troubleshooting/                  ← Debug guides
│   ├── auth-token-401.md
│   ├── webview2-issues.md
│   ├── velopack-setup-errors.md
│   ├── supabase-manifest-errors.md
│   ├── fullstack-operation-errors.md
│   └── build-errors.md
└── audit/                           ← Audit reports
    └── CODEBASE_AUDIT.md
```

## Quick Links

- [Start Here](START_HERE.md)
- [Implementation Plan](IMPLEMENTATION_PLAN.md)
- [Workflow Summary](WORKFLOW_SUMMARY.md)
- [Vibe Coding Guide](VIBE_CODING_GUIDE.md)
- [Codebase Audit](audit/CODEBASE_AUDIT.md)
- [System Overview](architecture/system-overview.md)
- [Client Architecture](architecture/client-architecture.md)
- [Tier Architecture](architecture/tier-architecture.md)
- [Manual Operations](manual/MANUAL_OPERATIONS.md)
- [Quick Release Checklist](manual/QUICK_RELEASE_CHECKLIST.md)
- [Development Workflow](dev/DEVELOPMENT_WORKFLOW.md)
- [Codebase Index](dev/CODEBASE_INDEX.md)
- [Development Roadmap](roadmap/DEVELOPMENT_ROADMAP.md)
- [Priority Tasks](roadmap/PRIORITY_TASKS.md)
- [Troubleshooting](troubleshooting/)
- [Release Overview](release/release-overview.md)
