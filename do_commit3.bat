@echo off
cd /d "C:\Users\lovetianyi\Desktop\AI coding\safe_terminal"
git add .
git commit -m "fix: config save path for single-file exe; update to v1.1; refresh sensitive_patterns.json"
git log --oneline -3
