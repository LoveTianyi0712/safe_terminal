@echo off
cd /d "C:\Users\lovetianyi\Desktop\AI coding\safe_terminal"
git add .
git commit -m "fix: unconditional save sync; delete actual files from disk in review window"
git log --oneline -4
