package com.example.jamkkaebi.friend.dto.response;

import com.example.jamkkaebi.artifact.domain.Era;

/**
 * 시대별 복원(완료) 유물 수.
 *
 * @param era            시대
 * @param completedCount 완료 유물 수
 */
public record EraCount(Era era, int completedCount) {
}
