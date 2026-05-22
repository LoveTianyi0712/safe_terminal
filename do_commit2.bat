@echo off
cd /d "C:\Users\lovetianyi\Desktop\AI coding\safe_terminal"
git add .
git commit -m "feat: fix save bug, optimize bank card regex, exclude VM adapters, distinguish Hyper-V, add sensitive file review window"
git log --oneline -3
