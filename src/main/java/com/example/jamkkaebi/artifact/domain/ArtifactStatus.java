package com.example.jamkkaebi.artifact.domain;

/**
 * 한 사용자가 가진 유물 한 종의 상태.
 *
 * <p>상자에서 뽑히면 {@link #IN_PROGRESS} 로 작업대에 오르고, 정령 수호(3페이즈)까지 마치면
 * {@link #COMPLETED} 가 된다.
 */
public enum ArtifactStatus {
    IN_PROGRESS, // 작업대 진행 중
    COMPLETED    // 정령 수호까지 완료
}
