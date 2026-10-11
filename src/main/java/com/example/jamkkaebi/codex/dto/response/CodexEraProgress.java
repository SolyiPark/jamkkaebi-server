package com.example.jamkkaebi.codex.dto.response;

import com.example.jamkkaebi.artifact.domain.Era;

/**
 * 한 시대의 진행률. 시대별 학습 흐름을 보완하기 위해 세 시대를 함께 보여 주는 데 쓴다.
 *
 * @param completedCount   완료 유물 수
 * @param acquiredCount    획득 유물 수 (진행 중 포함)
 * @param buildingAcquired 그 시대 건물 보유 여부
 */
public record CodexEraProgress(Era era, int completedCount, int acquiredCount, boolean buildingAcquired) {
}
