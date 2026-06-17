# AutoJMS Backlog

## P0 - Must fix before feature development

- [ ] Fix build blockers caused by missing modules/*.json
- [ ] Verify src/AutoJMS/AutoJMS.csproj Content Include behavior
- [ ] Stabilize JMS authToken 401 classifier
- [ ] Ensure WebView2 refresh runs on UI thread
- [ ] Disable BASE background inventory/database tracking
- [ ] Stabilize FullStackOperation grid lifecycle
- [ ] Validate release flow: GitHub binary + Supabase manifest

## P1 - Stability

- [ ] Mask token logs before production
- [ ] Normalize manifest schemas
- [ ] Create minimal test project
- [ ] Add TierRuntimePolicy tests
- [ ] Add JmsAuthTokenService tests
- [ ] Add version-latest manifest parser tests

## P2 - Architecture cleanup

- [ ] Split MainForm responsibilities gradually
- [ ] Split FullStackOperation services gradually
- [ ] Consolidate duplicate config/settings services
- [ ] Consolidate hash/integrity services
- [ ] Review ModuleSystem necessity

## P3 - Operation Center

- [ ] SLA Engine
- [ ] Risk Engine
- [ ] Operation Center realtime grid
- [ ] Supabase realtime cache strategy
- [ ] DataGridView 100k rows optimization


