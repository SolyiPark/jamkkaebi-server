package com.example.jamkkaebi.codex.dto.response;

import java.util.List;

/**
 * 도감 첫 화면.
 *
 * <p>수집률은 <b>유물만으로</b> 계산한다 — 건물은 시대 유물을 다 모으면 딸려 오므로 따로 센다.
 *
 * @param eras               삼국 → 고려 → 조선 순서로 항상 3개
 * @param completedArtifacts 전체 완료 유물 수
 * @param collectionRate     수집률(%)
 * @param buildingsAcquired  획득 건물 수
 */
public record CodexSummaryResponse(
        List<CodexEraProgress> eras,
        int completedArtifacts,
        int collectionRate,
        int buildingsAcquired
) {
}
