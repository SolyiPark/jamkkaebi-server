package com.example.jamkkaebi.codex.service;

import com.example.jamkkaebi.codex.dto.response.CodexBuildingView;
import com.example.jamkkaebi.codex.dto.response.CodexCardView;
import com.example.jamkkaebi.codex.dto.response.CodexEraProgress;
import com.example.jamkkaebi.codex.dto.response.CodexEraResponse;
import com.example.jamkkaebi.codex.dto.response.CodexRelicResponse;
import com.example.jamkkaebi.codex.dto.response.CodexSummaryResponse;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.response.FieldError;
import com.example.jamkkaebi.global.time.GameClock;
import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.Era;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.domain.UserBuilding;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.repository.UserBuildingRepository;
import com.example.jamkkaebi.relic.repository.UserRelicRepository;
import com.example.jamkkaebi.relic.service.ClarityPolicy;
import com.example.jamkkaebi.relic.service.RelicCatalog;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.service.UserProfileService;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDateTime;
import java.util.Arrays;
import java.util.Comparator;
import java.util.List;
import java.util.Map;
import java.util.function.Function;
import java.util.stream.Collectors;

/**
 * 도감 — 열람 전용.
 *
 * 성장·강화 확정은 전부 작업대에서 하고, 수집률도 조회할 때 보유 현황에서 집계한다.
 * 카드 상태도 마찬가지로 유물을 보유했는지·완료했는지에서 조회할 때 읽는다.
 */
@Service
@Transactional(readOnly = true)
public class CodexService {

    private final UserProfileService userProfileService;
    private final UserRelicRepository userRelicRepository;
    private final UserBuildingRepository userBuildingRepository;
    private final RelicCatalog catalog;
    private final ClarityPolicy clarityPolicy;
    private final GameClock gameClock;

    public CodexService(UserProfileService userProfileService,
                        UserRelicRepository userRelicRepository,
                        UserBuildingRepository userBuildingRepository,
                        RelicCatalog catalog,
                        ClarityPolicy clarityPolicy,
                        GameClock gameClock) {
        this.userProfileService = userProfileService;
        this.userRelicRepository = userRelicRepository;
        this.userBuildingRepository = userBuildingRepository;
        this.catalog = catalog;
        this.clarityPolicy = clarityPolicy;
        this.gameClock = gameClock;
    }

    // 도감 첫 화면
    public CodexSummaryResponse summary(String friendCode) {
        User user = userProfileService.getByFriendCode(friendCode);
        Map<Integer, UserRelic> owned = ownedRelics(user.getId());
        Map<Integer, UserBuilding> buildings = ownedBuildings(user.getId());
        Map<Era, List<RelicMaster>> mastersByEra = catalog.allRelics().stream()
                .collect(Collectors.groupingBy(RelicMaster::getEra));

        // 삼국 - 고려 - 조선 (역사 순)
        List<CodexEraProgress> eras = Arrays.stream(Era.values())
                .map(era -> progressOf(era, mastersByEra.getOrDefault(era, List.of()), owned, buildings))
                .toList();

        int completed = eras.stream().mapToInt(CodexEraProgress::completedCount).sum();
        int buildingsAcquired = (int) eras.stream().filter(CodexEraProgress::buildingAcquired).count();
        return new CodexSummaryResponse(eras, completed, rateOf(completed, RelicMaster.TOTAL), buildingsAcquired);
    }

    // 한 시대의 카드 3장과 건물 슬롯
    public CodexEraResponse era(String friendCode, Era era) {
        User user = userProfileService.getByFriendCode(friendCode);
        Map<Integer, UserRelic> owned = ownedRelics(user.getId());
        LocalDateTime now = gameClock.now();

        List<RelicMaster> masters = catalog.allRelics().stream()
                .filter(master -> master.getEra() == era)
                .sorted(Comparator.comparing(RelicMaster::getId))
                .toList();
        List<CodexCardView> cards = masters.stream()
                .map(master -> {
                    UserRelic userRelic = owned.get(master.getId());
                    return CodexCardView.of(master, userRelic, clarityOf(userRelic, now));
                })
                .toList();

        int completed = (int) masters.stream()
                .map(master -> owned.get(master.getId()))
                .filter(userRelic -> userRelic != null && userRelic.isCompleted())
                .count();
        CodexBuildingView building = catalog.buildingOf(era)
                .map(master -> CodexBuildingView.of(master,
                        userBuildingRepository.findByUserIdAndBuildingId(user.getId(), master.getId()).orElse(null)))
                .orElse(null);

        return new CodexEraResponse(era, rateOf(completed, masters.size()), cards, building);
    }

    /**
     * 카드 상세.
     *
     * <p>없는 유물 번호만 거절하고({@code C001}), 보유하지 않았거나 완료하지 않은 카드는 가려서 돌려준다.
     * 도감에서는 잠긴 칸도 눌러 볼 수 있어야 한다.
     */
    public CodexRelicResponse relic(String friendCode, Integer relicId) {
        User user = userProfileService.getByFriendCode(friendCode);
        RelicMaster master = catalog.findRelic(relicId)
                .orElseThrow(() -> new BusinessException(ErrorCode.INVALID_INPUT,
                        List.of(new FieldError("relicId", "존재하지 않는 유물입니다."))));
        UserRelic owned = userRelicRepository.findByUserIdAndRelicId(user.getId(), relicId)
                .orElse(null);
        return CodexRelicResponse.of(master, owned, clarityOf(owned, gameClock.now()));
    }

    private CodexEraProgress progressOf(Era era,
                                        List<RelicMaster> masters,
                                        Map<Integer, UserRelic> owned,
                                        Map<Integer, UserBuilding> buildings) {
        int acquired = 0;
        int completed = 0;
        for (RelicMaster master : masters) {
            UserRelic userRelic = owned.get(master.getId());
            if (userRelic == null) {
                continue;
            }
            acquired++;
            if (userRelic.isCompleted()) {
                completed++;
            }
        }
        boolean buildingAcquired = catalog.buildingOf(era)
                .map(building -> buildings.containsKey(building.getId()))
                .orElse(false);
        return new CodexEraProgress(era, completed, acquired, buildingAcquired);
    }

    private Map<Integer, UserRelic> ownedRelics(Long userId) {
        return userRelicRepository.findAllByUserId(userId).stream()
                .collect(Collectors.toMap(UserRelic::getRelicId, Function.identity()));
    }

    private Map<Integer, UserBuilding> ownedBuildings(Long userId) {
        return userBuildingRepository.findAllByUserId(userId).stream()
                .collect(Collectors.toMap(UserBuilding::getBuildingId, Function.identity()));
    }

    // 보유하지 않은 유물은 선명도 없음
    private Clarity clarityOf(UserRelic owned, LocalDateTime now) {
        return owned == null ? null : clarityPolicy.clarityOf(owned, now);
    }

    // 수집률(%)
    private static int rateOf(int completed, int total) {
        return total == 0 ? 0 : completed * 100 / total;
    }
}
