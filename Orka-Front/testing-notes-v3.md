# Testing Notes V3

## Wiki Drawer - WORKING
- Breadcrumb: Home / Wiki / AI Concepts / Supervised Learning - WORKING
- Title with ## heading - WORKING
- Content with bold, inline code - WORKING
- Algorithm Comparison table - visible but needs proper borders (pipe format, no grid lines)
- Code block with PYTHON label - WORKING, looks good
- Copy button - visible
- Edit Page button - visible
- Orka AI chat bubble (bottom right) - visible at index 32

## Issues Found:
1. Tables in wiki drawer render as pipe text, not proper HTML tables with borders
2. Tables in chat messages also render as pipe text (Comparison Table section)
3. Need to fix react-markdown table rendering - likely needs remark-gfm plugin
4. Quiz card hasn't appeared yet (random chance) - need to test more

## Next Steps:
- Install remark-gfm for proper table rendering
- Fix table styling in both ChatMessage and WikiDrawer
- Test quiz card
- Test landing page video
