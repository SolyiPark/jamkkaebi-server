package com.example.jamkkaebi.purification.dto.response;

import com.example.jamkkaebi.artifact.domain.Difficulty;
import com.example.jamkkaebi.artifact.domain.EntryRoute;
import com.example.jamkkaebi.artifact.domain.Phase;

import java.math.BigDecimal;
import java.time.LocalDateTime;

/**
 * 발급된 정화 세션.
 *
 * @param sessionToken          결과 보고 경로에 쓰는 1회용 토큰
 * @param expiresAt             이 시각이 지나면 보고할 수 없다
 * @param perfectBonusEligible  완벽 정화 보너스 대상인지. 최초 정화에는 적용하지 않는다
 */
public record PurificationStartResponse(
        String sessionToken,
        LocalDateTime expiresAt,
        EntryRoute entryRoute,
        Phase phase,
        Difficulty difficulty,
        BoardView board,
        BigDecimal rewardMultiplier,
        boolean perfectBonusEligible
) {
}
