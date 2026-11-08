"""Run the reviewed offline script with a process-only, non-echoed credential."""
import getpass
import os
import sys
import voice_pipeline as pipeline


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    if not os.environ.get("VUILABS_API_KEY"):
        os.environ["VUILABS_API_KEY"] = getpass.getpass("VUI credential (hidden): ").strip()
    if not os.environ["VUILABS_API_KEY"]:
        raise SystemExit("Missing credential")
    script = pipeline.load(pipeline.DEFAULT_SCRIPT)
    texts = pipeline.localized()
    for row in script["lines"]:
        result = pipeline.validate_line(row, script, texts)
        if result != "ready":
            raise SystemExit(row["key"] + ": " + result)
    print("Starting reviewed Japanese Ayano batch; 1.1x, credentials stay in process memory.", flush=True)
    pipeline.generate(pipeline.DEFAULT_SCRIPT, None)
    result = pipeline.pack(pipeline.DEFAULT_SCRIPT)
    print("BATCH COMPLETE: " + str(len(result["lines"])) + " recordings, " + str(result["pending_lines"]) + " pending.", flush=True)


if __name__ == "__main__":
    main()
