package com.example.jamkkaebi.codex.service;

import com.example.jamkkaebi.artifact.domain.ArtifactMaster;
import com.example.jamkkaebi.artifact.domain.Clarity;
import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import com.example.jamkkaebi.artifact.domain.UserBuilding;
import com.example.jamkkaebi.artifact.repository.UserArtifactRepository;
import com.example.jamkkaebi.artifact.repository.UserBuildingRepository;
import com.example.jamkkaebi.artifact.service.ArtifactCatalog;
import com.example.jamkkaebi.artifact.service.ClarityPolicy;
import com.example.jamkkaebi.codex.dto.response.CodexArtifactResponse;
import com.example.jamkkaebi.codex.dto.response.CodexBuildingView;
import com.example.jamkkaebi.codex.dto.response.CodexCardView;
import com.example.jamkkaebi.codex.dto.response.CodexEraProgress;
import com.example.jamkkaebi.codex.dto.response.CodexEraResponse;
import com.example.jamkkaebi.codex.dto.response.CodexSummaryResponse;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.response.FieldError;
import com.example.jamkkaebi.global.time.GameClock;
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
    private final UserArtifactRepository userArtifactRepository;
    private final UserBuildingRepository userBuildingRepository;
    private final ArtifactCatalog catalog;
    private final ClarityPolicy clarityPolicy;
    private final GameClock gameClock;

    public CodexService(UserProfileService userProfileService,
                        UserArtifactRepository userArtifactRepository,
                        UserBuildingRepository userBuildingRepository,
                        ArtifactCatalog catalog,
                        ClarityPolicy clarityPolicy,
                        GameClock gameClock) {
        this.userProfileService = userProfileService;
        this.userArtifactRepository = userArtifactRepository;
        this.userBuildingRepository = userBuildingRepository;
        this.catalog = catalog;
        this.clarityPolicy = clarityPolicy;
        this.gameClock = gameClock;
    }

    // 도감 첫 화면
    public CodexSummaryResponse summary(String friendCode) {
        User user = userProfileService.getByFriendCode(friendCode);
        Map<Integer, UserArtifact> owned = ownedArtifacts(user.getId());
        Map<Integer, UserBuilding> buildings = ownedBuildings(user.getId());
        Map<Era, List<ArtifactMaster>> mastersByEra = catalog.allArtifacts().stream()
                .collect(Collectors.groupingBy(ArtifactMaster::getEra));

        // 삼국 - 고려 - 조선 (역사 순)
        List<CodexEraProgress> eras = Arrays.stream(Era.values())
                .map(era -> progressOf(era, mastersByEra.getOrDefault(era, List.of()), owned, buildings))
                .toList();

        int completed = eras.stream().mapToInt(CodexEraProgress::completedCount).sum();
        int buildingsAcquired = (int) eras.stream().filter(CodexEraProgress::buildingAcquired).count();
        return new CodexSummaryResponse(eras, completed, rateOf(completed, ArtifactMaster.TOTAL), buildingsAcquired);
    }

    // 한 시대의 카드 3장과 건물 슬롯
    public CodexEraResponse era(String friendCode, Era era) {
        User user = userProfileService.getByFriendCode(friendCode);
        Map<Integer, UserArtifact> owned = ownedArtifacts(user.getId());
        LocalDateTime now = gameClock.now();

        List<ArtifactMaster> masters = catalog.allArtifacts().stream()
                .filter(master -> master.getEra() == era)
                .sorted(Comparator.comparing(ArtifactMaster::getId))
                .toList();
        List<CodexCardView> cards = masters.stream()
                .map(master -> {
                    UserArtifact userArtifact = owned.get(master.getId());
                    return CodexCardView.of(master, userArtifact, clarityOf(userArtifact, now));
                })
                .toList();

        int completed = (int) masters.stream()
                .map(master -> owned.get(master.getId()))
                .filter(userArtifact -> userArtifact != null && userArtifact.isCompleted())
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
    public CodexArtifactResponse artifact(String friendCode, Integer artifactId) {
        User user = userProfileService.getByFriendCode(friendCode);
        ArtifactMaster master = catalog.findArtifact(artifactId)
                .orElseThrow(() -> new BusinessException(ErrorCode.INVALID_INPUT,
                        List.of(new FieldError("artifactId", "존재하지 않는 유물입니다."))));
        UserArtifact owned = userArtifactRepository.findByUserIdAndArtifactId(user.getId(), artifactId)
                .orElse(null);
        return CodexArtifactResponse.of(master, owned, clarityOf(owned, gameClock.now()));
    }

    private CodexEraProgress progressOf(Era era,
                                        List<ArtifactMaster> masters,
                                        Map<Integer, UserArtifact> owned,
                                        Map<Integer, UserBuilding> buildings) {
        int acquired = 0;
        int completed = 0;
        for (ArtifactMaster master : masters) {
            UserArtifact userArtifact = owned.get(master.getId());
            if (userArtifact == null) {
                continue;
            }
            acquired++;
            if (userArtifact.isCompleted()) {
                completed++;
            }
        }
        boolean buildingAcquired = catalog.buildingOf(era)
                .map(building -> buildings.containsKey(building.getId()))
                .orElse(false);
        return new CodexEraProgress(era, completed, acquired, buildingAcquired);
    }

    private Map<Integer, UserArtifact> ownedArtifacts(Long userId) {
        return userArtifactRepository.findAllByUserId(userId).stream()
                .collect(Collectors.toMap(UserArtifact::getArtifactId, Function.identity()));
    }

    private Map<Integer, UserBuilding> ownedBuildings(Long userId) {
        return userBuildingRepository.findAllByUserId(userId).stream()
                .collect(Collectors.toMap(UserBuilding::getBuildingId, Function.identity()));
    }

    // 보유하지 않은 유물은 선명도 없음
    private Clarity clarityOf(UserArtifact owned, LocalDateTime now) {
        return owned == null ? null : clarityPolicy.clarityOf(owned, now);
    }

    // 수집률(%)
    private static int rateOf(int completed, int total) {
        return total == 0 ? 0 : completed * 100 / total;
    }
}
