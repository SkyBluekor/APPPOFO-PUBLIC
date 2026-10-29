# Synthetic inference fixture

`synthetic-face.png` is a newly generated fictional adult portrait created with OpenAI ImageGen on 2026-10-29 for this recovery task. It is not a captured person, an original private profile photo, or an approval to use a real person's face. It is separate from the 170 existing UI PNG files, whose pixels were preserved.

The validation loads this fixture into the actual Unity camera orientation/crop and native alignment code, then runs the official YuNet/SFace weights on CPU and GPU. The report contains only dimensions, confidence, norms and similarity summaries; no face embedding vector or profile database is published.

Passing this fixture proves the execution path. It does not establish real-world identity accuracy, liveness, demographic performance, FAR/FRR, or device-camera behavior.

Output-rights reference: [OpenAI Terms of Use](https://openai.com/policies/row-terms-of-use/).
