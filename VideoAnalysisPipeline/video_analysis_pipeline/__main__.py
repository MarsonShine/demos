from video_analysis_pipeline.bootstrap import bootstrap_and_run
from video_analysis_pipeline.cli import main


if __name__ == "__main__":
    exit_code = bootstrap_and_run(main)
    raise SystemExit(exit_code)
